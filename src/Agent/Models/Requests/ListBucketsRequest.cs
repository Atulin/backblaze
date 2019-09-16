﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

using Newtonsoft.Json;

namespace Bytewizer.Backblaze.Models
{
    /// <summary>
    /// Contains information to create a <see cref="ListBucketsRequest"/>.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay, nq}")]
    public class ListBucketsRequest : IEquatable<ListBucketsRequest>, IRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ListBucketsRequest"/> class.
        /// </summary>
        /// <param name="accountId">The account id.</param>
        public ListBucketsRequest(string accountId)
        {
            // Validate required arguments
            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException("Argument can not be null, empty, or consist only of white-space characters.", nameof(accountId));
            
            // Initialize and set required properties
            AccountId = accountId;
        }

        /// <summary>
        /// The account id.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string AccountId { get; private set; }

        /// <summary>
        /// When bucketId is specified, the result will be a list containing just this bucket,
        /// if it's present in the account, or no buckets if the account does not have a bucket with this ID. 
        /// </summary>
        public string BucketId { get; set; }

        /// <summary>
        /// When bucketName is specified, the result will be a list containing just this bucket, 
        /// if it's present in the account, or no buckets if the account does not have a bucket with this ID.  
        /// </summary>
        public string BucketName { get; set; }

        /// <summary>
        /// If present, B2 will use it as a filter for bucket types returned in the list buckets response. If not present,
        /// only buckets with bucket types "allPublic", "allPrivate" and "snapshot" will be returned. A special filter 
        /// value of ["all"] will return all bucket types. If present, it must be in the form of a json array of strings
        /// containing valid bucket types in quotes and separated by a comma. Valid bucket types include "allPrivate",
        /// "allPublic", "snapshot", and other values added in the future. A bad request error will be returned if "all" 
        /// is used with other bucketTypes, bucketTypes is empty, or invalid bucketTypes are requested. 
        /// </summary>
        public string BucketType { get; set; }

        /// <summary>
        /// Converts the value of this instance to a memory cache key.
        /// </summary>
        public string ToCacheKey()
        {
            return $"{GetType().Name}--{GetHashCode().ToString()}";
        }

        ///	<summary>
        ///	Debugger display for this object.
        ///	</summary>
        [JsonIgnore]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebuggerDisplay
        {
            get { return $"{{{nameof(AccountId)}: {AccountId}}}"; }
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ListBucketsRequest);
        }

        public bool Equals(ListBucketsRequest other)
        {
            return other != null &&
                   AccountId == other.AccountId &&
                   BucketId == other.BucketId &&
                   BucketName == other.BucketName &&
                   BucketType == other.BucketType;
        }

        public override int GetHashCode()
        {
            var hashCode = 248776742;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(AccountId);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(BucketId);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(BucketName);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(BucketType);
            return hashCode;
        }

        public static bool operator ==(ListBucketsRequest left, ListBucketsRequest right)
        {
            return EqualityComparer<ListBucketsRequest>.Default.Equals(left, right);
        }

        public static bool operator !=(ListBucketsRequest left, ListBucketsRequest right)
        {
            return !(left == right);
        }
    }
}
