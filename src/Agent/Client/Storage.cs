﻿using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Security.Authentication;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

using Polly;

using Bytewizer.Backblaze.Models;
using Bytewizer.Backblaze.Extensions;


namespace Bytewizer.Backblaze.Client
{
    /// <summary>
    /// Represents a base implementation which uses <see cref="HttpClient"/> for making HTTP requests.
    /// </summary>
    public abstract partial class Storage : DisposableObject
    {
        //TODO: Multithreading uploads/download for large file parts.

        #region Constants

        /// <summary>
        /// Represents the default number of times the client will retry failed requests before timing out.
        /// </summary>
        public const int DefaultRetryCount = 3;

        /// <summary>
        /// Represents the default number of parallel upload connections established.
        /// </summary>
        public const int DefaultUploadConnections = 1;

        /// <summary>
        /// Represents the default number of parallel download connections established.
        /// </summary>
        public const int DefaultDownloadConnections = 1;

        /// <summary>
        /// Represents the default upload url cache time to live (TTL) in seconds.
        /// </summary>
        public const int DefaultUploadUrlTTL = 3600;

        /// <summary>
        /// Represents the default upload part url cache time to live (TTL) in seconds.
        /// </summary>
        public const int DefaultUploadPartUrlTTL = 3600;

        #endregion

        #region Lifetime

        /// <summary>
        /// Initializes a new instance of the <see cref="Storage"/> class.
        /// </summary>
        public Storage() : this(null, null, null) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="Storage"/> class.
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> used for making requests.</param>
        public Storage(HttpClient httpClient, ILogger<Storage> logger, IMemoryCache cache)
        {
            _httpClient = httpClient ?? new HttpClient();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());

            DownloadPolicy = CreateDownloadPolicy();
            UploadPolicy = CreateUploadPolicy();
            InvokePolicy = CreateInvokePolicy();
        }

        #region IDisposable

        /// <summary>
        /// Frees resources owned by this instance.
        /// </summary>
        /// <param name="disposing">
        /// True when called via <see cref="IDisposable.Dispose()"/>, false when called from the finalizer.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            // Only managed resources to dispose
            if (!disposing)
                return;

            // Dispose owned objects
            _httpClient?.Dispose();
        }

        #endregion IDisposable

        #endregion

        #region Protected Fields

        /// <summary>
        /// <see cref="HttpClient"/> for making HTTP requests.
        /// </summary>
        protected readonly HttpClient _httpClient;

        /// <summary>
        /// <see cref="Logger"/> for application logging.
        /// </summary>
        protected readonly ILogger _logger;
        
        /// <summary>
        /// <see cref="MemoryCache"/> for application caching.
        /// </summary>
        public IMemoryCache _cache;

        #endregion

        #region Public Properties

        /// <summary>
        /// Json serializer.
        /// </summary>
        internal JsonSerializer JsonSerializer { get; } = new JsonSerializer();

        /// <summary>
        /// The account information returned from the Backblaze B2 server.
        /// </summary>
        public AccountInfo AccountInfo { get; } = new AccountInfo();

        /// <summary>
        /// The authorization token to use with all calls other than <see cref="AuthorizeAccountAync"/>. 
        /// This authorization token is valid for at most 24 hours.
        /// </summary>
        public AuthToken AuthToken { get; private set; }

        /// <summary>
        /// This is for testing use only and not recomended for production environments. Sets "X-Bx-Test-Mode" headers used for debugging and testing.  
        /// Setting it to "fail_some_uploads", "expire_some_account_authorization_tokens" or "force_cap exceeded" will cause the
        /// server to return specific errors used for testing.
        /// </summary>
        public string TestMode { get; set; } = string.Empty;

        /// <summary>
        /// The number of times the client will retry failed authentication requests before timing out.
        /// </summary>
        public int RetryCount { get; set; } = DefaultRetryCount;

        /// <summary>
        /// The maxium number of parallel upload connections established.
        /// </summary>
        public int UploadConnections { get; set; } = DefaultUploadConnections;

        /// <summary>
        /// Upload cutoff size for switching to chunked parts in bits.
        /// </summary>
        public FileSize UploadCutoffSize { get; set; } = FileSize.DefaultUploadCutoffSize;

        /// <summary>
        /// Upload part size of chunked parts in bits.
        /// </summary>
        public FileSize UploadPartSize { get; set; } = FileSize.DefaultUploadPartSize;

        /// <summary>
        /// The maxium number of parallel download connections established.
        /// </summary>
        public int DownloadConnections { get; set; } = DefaultDownloadConnections;
        /// <summary>
        /// Download cutoff size for switching to chunked parts in bits.
        /// </summary>
        public FileSize DownloadCutoffSize { get; set; } = FileSize.DefaultDownloadCutoffSize;

        /// <summary>
        /// Download part size of chunked parts in bits.
        /// </summary>
        public FileSize DownloadPartSize { get; set; } = FileSize.DefaultDownloadPartSize;

        #endregion

        #region Private Properties

        /// <summary>
        /// The key identifier used to authenticate to the Backblaze B2 Cloud Storage service. 
        /// </summary>
        protected string KeyId { get; set; }

        /// <summary>
        /// The secret part of the key used to authenticate.
        /// </summary>
        protected string ApplicationKey { get; set; }

        /// <summary>
        /// Retry policy used for downloading.
        /// </summary>
        protected IAsyncPolicy DownloadPolicy { get; set; }

        /// <summary>
        /// Retry policy used for uploading.
        /// </summary>
        protected IAsyncPolicy UploadPolicy { get; set; }

        /// <summary>
        /// Retry policy used for invoking post requests.
        /// </summary>
        protected IAsyncPolicy InvokePolicy { get; set; }
        
        #endregion

        #region Public Methods

        #region Authorize Account

        /// <summary>
        /// Connect to Backblaze B2 Cloud storage and initialize account settings.
        /// </summary>
        /// <param name="keyId">The identifier for the key.</param>
        /// <param name="applicationKey">The secret part of the key.</param>
        public void Connect(string keyId, string applicationKey)
        {
            ConnectAsync(keyId, applicationKey).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Connect to Backblaze B2 Cloud storage and initialize account settings.
        /// </summary>
        /// <param name="keyId">The identifier for the key.</param>
        /// <param name="applicationKey">The secret part of the key.</param>
        public async Task ConnectAsync(string keyId, string applicationKey)
        {
            KeyId = keyId;
            ApplicationKey = applicationKey;

            _cache.Remove(CacheKeys.UploadUrl);
            _cache.Remove(CacheKeys.UploadPartUrl);

            var results = await AuthorizeAccountAync(keyId, applicationKey, CancellationToken.None);
            if (results.IsSuccessStatusCode)
            {
                AuthToken = new AuthToken(results.Response.AuthorizationToken)
                {
                    Allowed = results.Response.Allowed
                };

                AccountInfo.ApiUrl = new Uri($"{results.Response.ApiUrl}b2api/v2/");
                AccountInfo.DownloadUrl = new Uri($"{results.Response.DownloadUrl}");
                AccountInfo.AccountId = results.Response.AccountId;
                AccountInfo.AbsoluteMinimumPartSize = results.Response.AbsoluteMinimumPartSize;
                AccountInfo.RecommendedPartSize = results.Response.RecommendedPartSize;

                _logger.LogInformation("Agent successfully authenticated account.");
            }
        }

        #endregion

        #region Upload Stream

        /// <summary>
        /// Uploads file content by bucket id. 
        /// </summary>
        /// <param name="request">The upload file request content to send.</param>
        /// <param name="content">The upload content to receive.</param>
        /// <param name="progress">A progress action which fires every time the write buffer is cycled.</param>
        /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
        public async Task<IApiResults<UploadFileResponse>> UploadByIdAsync
            (UploadFileByBucketIdRequest request, Stream content, IProgress<ICopyProgress> progress, CancellationToken cancel)
        {
            if (content.Length < GetCutoffSize(UploadCutoffSize, UploadPartSize))
            {
                var urlRequest = new GetUploadUrlRequest(request.BucketId);
                var urlResults = await GetUploadUrlAsync(urlRequest, TimeSpan.FromSeconds(DefaultUploadUrlTTL), cancel);

                if (urlResults.IsSuccessStatusCode)
                {
                    var response = urlResults.Response;
                    var fileRequest = new UploadFileRequest(response.UploadUrl, request.FileName, response.AuthorizationToken)
                    {
                        ContentType = request.ContentType,
                        FileInfo = request.FileInfo
                    };

                    return await UploadFileAsync(fileRequest, content, progress, cancel);
                }

                return new ApiResults<UploadFileResponse>(urlResults.HttpResponse, urlResults.Error);
            }
            else
            {
                var largeFileRequest = new UploadLargeFileRequest(request.BucketId, request.FileName, content)
                {
                    ContentType = request.ContentType,
                    FileInfo = request.FileInfo
                };

                return await UploadLargeFileAsync(largeFileRequest, progress, cancel);
            }
        }

        #endregion

        #region Download Stream

        /// <summary>
        /// Downloads a specific version of content by file id. 
        /// </summary>
        /// <param name="request">The download file request to send.</param>
        /// <param name="content">The download content to receive.</param>
        /// <param name="progress">A progress action which fires every time the write buffer is cycled.</param>
        /// <param name="cancel">The cancellation token to cancel operation.</param>
        public async Task<IApiResults<DownloadFileResponse>> DownloadByIdAsync
            (DownloadFileByIdRequest request, Stream content, IProgress<ICopyProgress> progress, CancellationToken cancel)
        {
            var fileRequest = new DownloadFileByIdRequest(request.FileId);
            var fileResults = await DownloadFileByIdAsync(fileRequest, cancel);
            if (fileResults.IsSuccessStatusCode)
            {
                if (fileResults.Response.ContentLength < DownloadCutoffSize)
                {
                    return await DownloadFileByIdAsync(request, content, progress, cancel);
                }
                else
                {
                    return await DownloadLargeFileAsync(fileRequest, fileResults, content, progress, cancel);
                }
            }

            return fileResults;
        }

        /// <summary>
        /// Downloads the most recent version of content by bucket and file name. 
        /// </summary>
        /// <param name="request">The download file request to send.</param>
        /// <param name="content">The download content to receive.</param>
        /// <param name="progress">A progress action which fires every time the write buffer is cycled.</param>
        /// <param name="cancel">The cancellation token to cancel operation.</param>
        public async Task<IApiResults<DownloadFileResponse>> DownloadAsync
            (DownloadFileByNameRequest request, Stream content, IProgress<ICopyProgress> progress, CancellationToken cancel)
        {
            var fileRequest = new DownloadFileByNameRequest(request.BucketName, request.FileName);
            var fileResults = await DownloadFileByNameAsync(fileRequest, cancel);

            if (fileResults.IsSuccessStatusCode)
            {
                if (fileResults.Response.ContentLength < GetCutoffSize(DownloadCutoffSize, DownloadPartSize))
                {
                    return await DownloadFileByNameAsync(request, content, progress, cancel);
                }
                else
                {
                    return await DownloadLargeFileAsync(fileRequest, fileResults, content, progress, cancel);
                }
            }

            return fileResults;
        }

        #endregion

        #endregion

        #region Private Methods

        /// <summary>
        /// Downloads the most recent version of a large file in chunked parts. 
        /// </summary>
        /// <param name="request">The download file request content to send.</param>
        /// <param name="content">The download content to receive.</param>
        /// <param name="progress">A progress action which fires every time the write buffer is cycled.</param>
        /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
        private async Task<IApiResults<DownloadFileResponse>> DownloadLargeFileAsync
            (DownloadFileByIdRequest request, IApiResults<DownloadFileResponse> results, Stream content, IProgress<ICopyProgress> progress, CancellationToken cancellationToken)
        {
            var parts = GetContentParts(results.Response.ContentLength, DownloadPartSize);

            foreach (var part in parts)
            {
                var mmultiStream = new MultiStream(content, part.Position, part.Length);
                var partReqeust = new DownloadFileByIdRequest(request.FileId)
                {
                    Range = new RangeHeaderValue(part.Position, part.Position + part.Length - 1)
                };

                var partResults = await DownloadFileByIdAsync(partReqeust, mmultiStream, progress, cancellationToken);
                if (!partResults.IsSuccessStatusCode)
                {
                    return new ApiResults<DownloadFileResponse>(partResults.HttpResponse, partResults.Error);
                }
            }

            return results;
        }

        /// <summary>
        /// Downloads the most recent version of a large file in chunked parts. 
        /// </summary>
        /// <param name="request">The download file request content to send.</param>
        /// <param name="content">The download content to receive.</param>
        /// <param name="progress">A progress action which fires every time the write buffer is cycled.</param>
        /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
        private async Task<IApiResults<DownloadFileResponse>> DownloadLargeFileAsync
            (DownloadFileByNameRequest request, IApiResults<DownloadFileResponse> results, Stream content, IProgress<ICopyProgress> progress, CancellationToken cancellationToken)
        {
            var parts = GetContentParts(results.Response.ContentLength, DownloadPartSize);

            foreach (var part in parts)
            {
                var mmultiStream = new MultiStream(content, part.Position, part.Length);
                var partReqeust = new DownloadFileByNameRequest(request.BucketName, request.FileName)
                {
                    Range = new RangeHeaderValue(part.Position, part.Position + part.Length - 1)
                };

                var partResults = await DownloadFileByNameAsync(partReqeust, mmultiStream, progress, cancellationToken);
                if (!partResults.IsSuccessStatusCode)
                {
                    return new ApiResults<DownloadFileResponse>(partResults.HttpResponse, partResults.Error);
                }
            }

            return results;
        }

        /// <summary>
        /// Uploads a large file in chunked parts. 
        /// </summary>
        /// <param name="request">The upload file request content to send.</param>
        /// <param name="progress">A progress action which fires every time the write buffer is cycled.</param>
        /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
        private async Task<IApiResults<UploadFileResponse>> UploadLargeFileAsync
            (UploadLargeFileRequest request, IProgress<ICopyProgress> progress, CancellationToken cancellationToken)
        {
            if (request.ContentLength < AccountInfo.AbsoluteMinimumPartSize)
                throw new ArgumentOutOfRangeException($"Argument must be a minimum of {AccountInfo.AbsoluteMinimumPartSize} bytes long.", nameof(request.ContentLength));

            List<string> sha1Hash = new List<string>();

            var parts = GetStreamParts(request.ContentStream, (long)GetPartSize(UploadPartSize));
            if (parts.Count == 0)
                throw new ApiException($"The number of large file parts could not be determined from stream.");

            var fileRequest = new StartLargeFileRequest(request.BucketId, request.FileName)
            {
                ContentType = request.ContentType,
                FileInfo = request.FileInfo
            };

            fileRequest.FileInfo.SetLargeFileSha1(request.ContentStream.ToSha1());

            var fileResults = await StartLargeFileAsync(fileRequest, cancellationToken);
            if (fileResults.IsSuccessStatusCode)
            {
                var urlRequest = new GetUploadPartUrlRequest(fileResults.Response.FileId);
                var urlResults = await GetUploadPartUrlAsync(urlRequest,TimeSpan.FromSeconds(DefaultUploadPartUrlTTL), cancellationToken);
                if (fileResults.IsSuccessStatusCode)
                {
                    foreach (var part in parts)
                    {
                        var partStream = new PartialStream(request.ContentStream, part.Position, part.Length);
                        var partReqeust = new UploadPartRequest(urlResults.Response.UploadUrl, part.PartNumber, urlResults.Response.AuthorizationToken);

                        var partResults = await UploadPartAsync(partReqeust, partStream, progress, cancellationToken);
                        if (partResults.IsSuccessStatusCode)
                        {
                            sha1Hash.Add(partResults.Response.ContentSha1);
                        }
                        else
                        {
                            return new ApiResults<UploadFileResponse>(partResults.HttpResponse, partResults.Error);
                        }
                    }
                }
                else
                {
                    return new ApiResults<UploadFileResponse>(fileResults.HttpResponse, fileResults.Error);
                }

                var finishRequest = new FinishLargeFileRequest(fileResults.Response.FileId, sha1Hash);
                var finishResults = await FinishLargeFileAsync(finishRequest, cancellationToken);
                if (finishResults.IsSuccessStatusCode)
                {
                    var infoRequest = new GetFileInfoRequest(fileResults.Response.FileId);
                    var infoResults = await GetFileInfoAsync(infoRequest, cancellationToken);
                    if (infoResults.IsSuccessStatusCode)
                    {
                        return finishResults;
                    }
                    else
                    {
                        return new ApiResults<UploadFileResponse>(infoResults.HttpResponse, infoResults.Error);
                    }
                }
                return new ApiResults<UploadFileResponse>(finishResults.HttpResponse, finishResults.Error);
            }
            return new ApiResults<UploadFileResponse>(fileResults.HttpResponse, fileResults.Error);
        }

        /// <summary>
        /// Gets cutoff size in bits for switching to chunked parts upload.
        /// </summary>
        private FileSize GetCutoffSize(FileSize cutoff, FileSize part)
        {
            FileSize cutoffSize;
            if (cutoff == 0)
            {
                cutoffSize = GetPartSize(part);
            }
            else
            {
                if (cutoff <= AccountInfo.AbsoluteMinimumPartSize)
                {
                    cutoffSize = AccountInfo.AbsoluteMinimumPartSize;
                }
                else
                {
                    cutoffSize = cutoff;
                }
            }
            return cutoffSize;
        }

        /// <summary>
        /// Gets part size in bits of large file chunked parts.
        /// </summary>
        private FileSize GetPartSize(FileSize part)
        {
            var func = new Func<string, string, Task>(ConnectAsync);

            FileSize partSize;
            if (part == 0)
            {
                partSize = AccountInfo.RecommendedPartSize;
            }
            else
            {
                if (part < AccountInfo.AbsoluteMinimumPartSize)
                {
                    partSize = AccountInfo.AbsoluteMinimumPartSize;
                }
                else
                {
                    partSize = part;
                }
            }
            return partSize;
        }

        /// <summary>
        /// Create a retry policy for upload execptions.
        /// </summary>
        private IAsyncPolicy CreateUploadPolicy()
        {
            var auth = CreateAuthenticationPolicy();
            var hash = CreateInvalidHashPolicy();
            var bulkhead = Policy.BulkheadAsync(UploadConnections, int.MaxValue);

            return Policy.WrapAsync(auth, hash, bulkhead);
        }

        /// <summary>
        /// Create a retry policy for download execptions.
        /// </summary>
        private IAsyncPolicy CreateDownloadPolicy()
        {
            var auth = CreateAuthenticationPolicy();
            var hash = CreateInvalidHashPolicy();
            var bulkhead = Policy.BulkheadAsync(DownloadConnections, int.MaxValue);

            return Policy.WrapAsync(auth, hash, bulkhead);
        }

        /// <summary>
        /// Create a retry policy for Invoke execptions.
        /// </summary>
        private IAsyncPolicy CreateInvokePolicy()
        {
            return CreateAuthenticationPolicy();
        }

        /// <summary>
        /// Create a retry policy for authentication execptions.
        /// </summary>
        private IAsyncPolicy CreateAuthenticationPolicy()
        {
            var auth = Policy
                .Handle<AuthenticationException>()
                .WaitAndRetryAsync(RetryCount,
                    retryAttempt => GetSleepDuration(retryAttempt),
                    onRetry: async (exception, timeSpan, count, context) =>
                    {
                        _logger.LogWarning($"{exception.Message} Retry attempt {count} waiting {timeSpan.TotalSeconds} seconds before next retry.");
                        await ConnectAsync(KeyId, ApplicationKey);
                    });

            return auth;
        }

        /// <summary>
        /// Create a retry policy for invalid hash execptions.
        /// </summary>
        private IAsyncPolicy CreateInvalidHashPolicy()
        {
            var hash = Policy
               .Handle<InvalidHashException>()
               .WaitAndRetryAsync(RetryCount,
                   retryAttempt => GetSleepDuration(retryAttempt),
                   onRetry: (exception, timeSpan, count, context) =>
                   {
                       _logger.LogWarning($"{exception.Message} Retry attempt {count} waiting {timeSpan.TotalSeconds} seconds before next retry.");
                   });

            return hash;
        }

        /// <summary>
        /// Get the duration to wait (exponential backoff) allowing an increasing wait time.
        /// </summary>
        /// <param name="retryAttempt">The retry attempt count.</param>
        public static TimeSpan GetSleepDuration(int retryAttempt)
        {
            Random jitterer = new Random();

            return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                    + TimeSpan.FromMilliseconds(jitterer.Next(10, 1000));
        }

        /// <summary>
        /// Gets the file parts to upload.
        /// </summary>
        /// <param name="content">The upload content stream.</param>
        /// <param name="partSize">The part size in bits.</param>
        private static HashSet<FileParts> GetStreamParts(Stream content, long partSize)
        {
            HashSet<FileParts> hashSet = new HashSet<FileParts>();

            long streamLength = (content.CanSeek ? content.Length : -1);

            if (streamLength == -1 || streamLength <= partSize)
                return hashSet;

            long parts = streamLength / partSize;

            for (int i = 0; i <= parts; i++)
            {
                hashSet.Add(
                    new FileParts()
                    {
                        PartNumber = i + 1,
                        Position = i * partSize,
                        Length = Math.Min(streamLength - (i * partSize), partSize)
                    }
                );
            }

            return hashSet;
        }

        /// <summary>
        /// Gets the content parts to download.
        /// </summary>
        /// <param name="contentLength">The download content length.</param>
        /// <param name="partSize">The part size in bits.</param>
        private static HashSet<FileParts> GetContentParts(long contentLength, FileSize partSize)
        {
            HashSet<FileParts> hashSet = new HashSet<FileParts>();

            if (contentLength == -1 || contentLength <= partSize)
                return hashSet;

            long parts = contentLength / (long)partSize;

            for (int i = 0; i <= parts; i++)
            {
                hashSet.Add(
                    new FileParts()
                    {
                        PartNumber = i + 1,
                        Position = i * (long)partSize,
                        Length = Math.Min(contentLength - (i * (long)partSize), (long)partSize)
                    }
                );
            }

            return hashSet;
        }

        #endregion
    }
}