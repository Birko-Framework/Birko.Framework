using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Communication.REST;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.REST
    /// </summary>
    public static class RestExamples
    {
        /// <summary>
        /// Example: Basic CRUD operations using RestClient
        /// </summary>
        public static async Task RunBasicExample()
        {
            ExampleOutput.WriteLine("=== REST Client Example ===\n");

            var client = new RestClient("https://api.example.com");
            client.DefaultContentType = "application/json";
            client.Timeout = 30000;
            client.DefaultHeaders["Accept"] = "application/json";

            // Log requests and responses
            client.OnRequest += (sender, e) =>
            {
                ExampleOutput.WriteLine($"  >> {e.Method} {e.Uri}");
            };

            client.OnResponse += (sender, e) =>
            {
                ExampleOutput.WriteLine($"  << {e.StatusCode}: {e.Content.Length} chars");
            };

            try
            {
                // GET
                ExampleOutput.WriteLine("1. GET /products/123");
                var product = await client.GetAsync("/products/123");
                ExampleOutput.WriteLine($"   Response: {product}\n");

                // POST with JSON body
                ExampleOutput.WriteLine("2. POST /products");
                var newProduct = "{\"name\":\"Widget\",\"price\":29.99}";
                var created = await client.PostAsync("/products", newProduct);
                ExampleOutput.WriteLine($"   Response: {created}\n");

                // PUT
                ExampleOutput.WriteLine("3. PUT /products/123");
                var updated = await client.PutAsync("/products/123", "{\"price\":24.99}");
                ExampleOutput.WriteLine($"   Response: {updated}\n");

                // DELETE
                ExampleOutput.WriteLine("4. DELETE /products/123");
                var deleted = await client.DeleteAsync("/products/123");
                ExampleOutput.WriteLine($"   Response: {deleted}\n");

                // PATCH
                ExampleOutput.WriteLine("5. PATCH /products/123");
                var patched = await client.PatchAsync("/products/123", "{\"stock\":50}");
                ExampleOutput.WriteLine($"   Response: {patched}");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"REST Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Authenticated requests using headers
        /// </summary>
        public static async Task RunAuthenticatedExample()
        {
            ExampleOutput.WriteLine("=== REST Authenticated Requests ===\n");

            var client = new RestClient("https://api.example.com");

            // Bearer token via default headers
            ExampleOutput.WriteLine("1. Bearer Token Authentication");
            client.DefaultHeaders["Authorization"] = "Bearer eyJhbGciOiJIUzI1NiJ9...";

            try
            {
                var profile = await client.GetAsync("/user/profile");
                ExampleOutput.WriteLine($"   Profile: {profile}\n");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"   Error: {ex.Message}\n");
            }

            // API key via per-request headers
            ExampleOutput.WriteLine("2. API Key Authentication");
            client.DefaultHeaders.Clear();
            var apiKeyHeaders = new Dictionary<string, string>
            {
                { "X-API-Key", "your-api-key-here" }
            };

            try
            {
                var data = await client.GetAsync("/data", headers: apiKeyHeaders);
                ExampleOutput.WriteLine($"   Data: {data}");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"   Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Client caching with GetClient for connection reuse
        /// </summary>
        public static async Task RunCachedClientExample()
        {
            ExampleOutput.WriteLine("=== REST Client Caching ===\n");

            // GetClient returns the same instance for the same base URI
            var client1 = RestClient.GetClient("https://api.example.com");
            var client2 = RestClient.GetClient("https://api.example.com");

            ExampleOutput.WriteLine($"Same instance: {ReferenceEquals(client1, client2)}"); // True

            client1.DefaultHeaders["User-Agent"] = "Birko.Framework/1.0";

            try
            {
                var result = await client1.GetAsync("/status");
                ExampleOutput.WriteLine($"Status: {result}");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Error: {ex.Message}");
            }

            // Clean up cached clients
            RestClient.RemoveClient("https://api.example.com");
            ExampleOutput.WriteLine("Removed cached client");

            // Or clear all cached clients
            RestClient.ClearCache();
            ExampleOutput.WriteLine("Cleared all cached clients");
        }
    }
}
