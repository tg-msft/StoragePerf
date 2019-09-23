﻿using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Common;
using CommandLine;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StoragePerfNet
{
    class Program
    {
        private const string _containerName = "testcontainer";
        private const string _blobName = "testblobupdown";
        
        public class Options
        {
            [Option("debug")]
            public bool Debug { get; set; }

            [Option('s', "size", Default = 10 * 1024, HelpText = "Size of message (in bytes)")]
            public int Size { get; set; }

            [Option('t', "maximumThreadCount", Default = -1)]
            public int MaximumThreadCount { get; set; }
        }

        static async Task Main(string[] args)
        {
            if (!GCSettings.IsServerGC)
            {
                throw new InvalidOperationException("Requires server GC");
            }

            var connectionString = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");

            await Parser.Default.ParseArguments<Options>(args).MapResult(
                async o => await Run(connectionString, o),
                errors => Task.CompletedTask);
        }

        static async Task Run(string connectionString, Options options)
        {
#if DEBUG
            if (!options.Debug)
            {
                throw new InvalidOperationException("Requires release configuration");
            }
#endif

            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            var httpClient = new HttpClient(httpClientHandler);

            var blobClientOptions = new BlobClientOptions();
            blobClientOptions.Transport = new HttpClientTransport(httpClient);

            var client = new BlobClient(connectionString, _containerName, _blobName, blobClientOptions);

            var parallelTransferOptions = new ParallelTransferOptions();
            if (options.MaximumThreadCount != -1)
            {
                parallelTransferOptions.MaximumThreadCount = options.MaximumThreadCount;
            }

            var payload = new byte[options.Size];
            // Initialize payload with stable random data since all-zeros may be compressed or optimized
            (new Random(0)).NextBytes(payload);
            var payloadStream = new MemoryStream(payload, writable: false);

            Console.WriteLine($"Uploading and downloading blob of size {options.Size} with {options.MaximumThreadCount} threads...");
            Console.WriteLine();

            var sw = new Stopwatch();
            while (true)
            {
                payloadStream.Seek(0, SeekOrigin.Begin);

                sw.Restart();
                await client.UploadAsync(payloadStream, parallelTransferOptions: parallelTransferOptions);
                sw.Stop();

                var elapsedSeconds = sw.Elapsed.TotalSeconds;
                var megabytesPerSecond = (options.Size / (1024 * 1024)) / elapsedSeconds;
                Console.WriteLine($"Uploaded {options.Size} bytes in {elapsedSeconds} seconds ({megabytesPerSecond} MB/s)");

                sw.Restart();
                await client.DownloadAsync(Stream.Null, parallelTransferOptions: parallelTransferOptions);
                sw.Stop();

                elapsedSeconds = sw.Elapsed.TotalSeconds;
                megabytesPerSecond = (options.Size / (1024 * 1024)) / elapsedSeconds;
                Console.WriteLine($"Downloaded {options.Size} bytes in {elapsedSeconds} seconds ({megabytesPerSecond} MB/s)");

                Console.WriteLine();
            }
        }
    }
}