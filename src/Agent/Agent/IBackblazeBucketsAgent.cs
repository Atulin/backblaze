﻿using System;
using System.Threading.Tasks;
using System.Collections.Generic;

using Bytewizer.Backblaze.Models;

namespace Bytewizer.Backblaze.Agent
{
    public interface IBackblazeBucketsAgent
    {
        Task<BucketItem> FirstAsync();
        Task<BucketItem> FirstAsync(Func<BucketItem, bool> predicate);

        Task<List<BucketItem>> ListAsync(ListBucketsRequest request, int cacheTTL);

        Task<IApiResults<ListBucketsResponse>> GetAsync();
        Task<IApiResults<ListBucketsResponse>> GetAsync(ListBucketsRequest request);
        Task<IApiResults<ListBucketsResponse>> GetAsync(string accountId);

        Task<IApiResults<CreateBucketResponse>> CreateAsync(CreateBucketRequest request);
        Task<IApiResults<CreateBucketResponse>> CreateAsync(string bucketName, BucketType bucketType);
        Task<IApiResults<CreateBucketResponse>> CreateAsync(string accountId, string bucketName, BucketType bucketType);

        Task<IApiResults<UpdateBucketResponse>> UpdateAsync(UpdateBucketRequest request);
        Task<IApiResults<UpdateBucketResponse>> UpdateAsync(string bucketId, BucketType bucketType);
        Task<IApiResults<UpdateBucketResponse>> UpdateAsync(string accountId, string bucketId, BucketType bucketType);

        Task<IApiResults<DeleteBucketResponse>> DeleteAsync(string bucketId);
        Task<IApiResults<DeleteBucketResponse>> DeleteAsync(string accountId, string bucketId);
    }
}
