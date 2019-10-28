﻿using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

using Bytewizer.Backblaze.Client;

namespace Bytewizer.HttpClient.Sample
{
    class Program
    {
        // Note: Run with Control + F5 to keep the console window open

        private static BackblazeAgent Client;

        static async Task Main(string[] args)
        {
            try
            {
                var options = new ClientOptions();

                var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder
                        .AddFilter("Microsoft", LogLevel.Warning)
                        .AddFilter("System", LogLevel.Warning)
                        .AddFilter("Bytewizer.Backblaze", LogLevel.Trace)
                        .AddDebug();
                });

                var cache = new MemoryCache(new MemoryCacheOptions());

                Client = new BackblazeAgent(options, loggerFactory, cache);
                
                await Client.ConnectAsync("[key_id]", "[application_key]");

                var buckets = await Client.Buckets.GetAsync();

                foreach (var bucket in buckets)
                    Console.WriteLine($"Bucket Name: {bucket.BucketName} - Type: {bucket.BucketType}");   
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }
    }
}
