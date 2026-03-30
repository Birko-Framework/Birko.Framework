using System;
using System.Net;
using System.Threading.Tasks;

namespace Birko.Framework.Examples.Communication
{
    // Note: These examples require Birko.Communication.SOAP to be referenced
    // Placeholder classes are provided below for demonstration

    // Placeholder classes - remove these when adding reference to Birko.Communication.SOAP
    public class SoapService
    {
        protected string ExtractSoapAction(string envelope) => string.Empty;
        protected string CreateResponseEnvelope(string body) => body;
        protected string CreateFaultResponse(string code, string message) => string.Empty;
        public virtual Task<string> ProcessRequestAsync(string soapEnvelope, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }

    // Custom event args classes
    public class RequestContext : EventArgs
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ServicePath { get; set; }
        public string? Action { get; set; }
    }

    public class ResponseContext : EventArgs
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? StatusCode { get; set; }
    }

    public class SoapServer
    {
        public SoapServer(object? logger) { }
        public void RegisterService(string path, SoapService service) { }
        public event EventHandler<RequestContext> OnRequest = delegate { };
        public Task StartAsync(string uri, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    public class SoapClient
    {
        public SoapClient(string url) { }
        public event EventHandler<RequestContext> OnRequest = delegate { };
        public event EventHandler<ResponseContext> OnResponse = delegate { };
        public string SendRequest(string action, string envelope) => string.Empty;
        public Task<string> SendRequestAsync(string action, string envelope) => Task.FromResult(string.Empty);
        public static SoapClient GetClient(string url) => new(url);
        public static void RemoveClient(string url) { }
        public static void ClearCache() { }
    }

    public class ConsoleLogger
    {
        public ConsoleLogger(string name) { }
    }

    /// <summary>
    /// Example SOAP service implementation
    /// </summary>
    public class CalculatorService : SoapService
    {
        public override async Task<string> ProcessRequestAsync(string soapEnvelope, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            var action = ExtractSoapAction(soapEnvelope);

            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(soapEnvelope);

                string result = action switch
                {
                    "Add" => HandleAdd(doc),
                    "Subtract" => HandleSubtract(doc),
                    "Multiply" => HandleMultiply(doc),
                    "Divide" => HandleDivide(doc),
                    _ => throw new SoapException($"Unknown action: {action}")
                };

                return CreateResponseEnvelope(result);
            }
            catch (Exception ex)
            {
                return CreateFaultResponse("Client", ex.Message);
            }
        }

        private static string HandleAdd(System.Xml.XmlDocument doc)
        {
            var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsmgr.AddNamespace("m", "http://tempuri.org/");

            var a = int.Parse(doc.SelectSingleNode("//m:a", nsmgr)!.InnerText);
            var b = int.Parse(doc.SelectSingleNode("//m:b", nsmgr)!.InnerText);

            return $"<m:AddResponse xmlns:m=\"http://tempuri.org/\"><m:AddResult>{a + b}</m:AddResult></m:AddResponse>";
        }

        private static string HandleSubtract(System.Xml.XmlDocument doc)
        {
            var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsmgr.AddNamespace("m", "http://tempuri.org/");

            var a = int.Parse(doc.SelectSingleNode("//m:a", nsmgr)!.InnerText);
            var b = int.Parse(doc.SelectSingleNode("//m:b", nsmgr)!.InnerText);

            return $"<m:SubtractResponse xmlns:m=\"http://tempuri.org/\"><m:SubtractResult>{a - b}</m:SubtractResult></m:SubtractResponse>";
        }

        private static string HandleMultiply(System.Xml.XmlDocument doc)
        {
            var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsmgr.AddNamespace("m", "http://tempuri.org/");

            var a = int.Parse(doc.SelectSingleNode("//m:a", nsmgr)!.InnerText);
            var b = int.Parse(doc.SelectSingleNode("//m:b", nsmgr)!.InnerText);

            return $"<m:MultiplyResponse xmlns:m=\"http://tempuri.org/\"><m:MultiplyResult>{a * b}</m:MultiplyResult></m:MultiplyResponse>";
        }

        private static string HandleDivide(System.Xml.XmlDocument doc)
        {
            var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsmgr.AddNamespace("m", "http://tempuri.org/");

            var a = int.Parse(doc.SelectSingleNode("//m:a", nsmgr)!.InnerText);
            var b = int.Parse(doc.SelectSingleNode("//m:b", nsmgr)!.InnerText);

            if (b == 0)
                throw new ArgumentException("Division by zero");

            return $"<m:DivideResponse xmlns:m=\"http://tempuri.org/\"><m:DivideResult>{a / b}</m:DivideResult></m:DivideResponse>";
        }
    }

    /// <summary>
    /// Custom exception for SOAP errors
    /// </summary>
    public class SoapException : Exception
    {
        public SoapException(string message) : base(message) { }
        public SoapException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Usage examples for Birko.Communication.SOAP
    /// </summary>
    public static class SoapUsageExamples
    {
        // Note: These methods work with the placeholder classes above
        // When you add the actual reference to Birko.Communication.SOAP,
        // these will use the real implementations

        /// <summary>
        /// Example: Hosting a SOAP server
        /// </summary>
        public static async Task RunServerExample()
        {
            var logger = new ConsoleLogger("SoapServer");
            var server = new SoapServer(logger);

            // Register the calculator service
            server.RegisterService("calculator.asmx", new CalculatorService());

            // Subscribe to request events for monitoring
            server.OnRequest += (sender, context) =>
            {
                ExampleOutput.WriteLine($"[{context.Timestamp}] Request to {context.ServicePath}");
            };

            // Start the server
            var cts = new System.Threading.CancellationTokenSource();
            ExampleOutput.WriteLine("Starting SOAP server on http://localhost:8080/soap/");
            await server.StartAsync("http://localhost:8080/soap/", cts.Token);

            ExampleOutput.WriteLine("Press ENTER to stop the server...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            await server.StopAsync();
        }

        /// <summary>
        /// Example: Consuming a SOAP service
        /// </summary>
        public static async Task RunClientExample()
        {
            var client = new SoapClient("http://localhost:8080/soap/calculator.asmx");

            // Subscribe to request/response events
            client.OnRequest += (sender, args) =>
            {
                ExampleOutput.WriteLine($"[{args.Timestamp}] Request: {args.Action ?? "N/A"}");
            };

            client.OnResponse += (sender, args) =>
            {
                ExampleOutput.WriteLine($"[{args.Timestamp}] Response: {args.StatusCode ?? "N/A"}");
            };

            // Build a SOAP request
            var soapRequest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
               xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
               xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <Add xmlns=""http://tempuri.org/"">
      <a>10</a>
      <b>5</b>
    </Add>
  </soap:Body>
</soap:Envelope>";

            try
            {
                // Send the request synchronously
                var response = client.SendRequest(
                    "http://tempuri.org/Add",
                    soapRequest
                );

                ExampleOutput.WriteLine("Response:");
                ExampleOutput.WriteLine(response);

                // Or send asynchronously
                var responseAsync = await client.SendRequestAsync(
                    "http://tempuri.org/Add",
                    soapRequest
                );
            }
            catch (WebException ex)
            {
                ExampleOutput.WriteLine($"SOAP Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Using cached client instances
        /// </summary>
        public static void RunCachedClientExample()
        {
            // Get a cached client (creates new if not exists)
            var client1 = SoapClient.GetClient("http://example.com/service.asmx");
            var client2 = SoapClient.GetClient("http://example.com/service.asmx");

            // Both references point to the same instance
            ExampleOutput.WriteLine(ReferenceEquals(client1, client2).ToString()); // True

            // Use the client
            var soapRequest = BuildSoapRequest();

            var response = client1.SendRequest(
                "http://tempuri.org/SomeAction",
                soapRequest
            );

            // Remove a specific client from cache
            SoapClient.RemoveClient("http://example.com/service.asmx");

            // Or clear all cached clients
            SoapClient.ClearCache();
        }

        private static string BuildSoapRequest()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
               xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
               xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <!-- Your SOAP body content here -->
  </soap:Body>
</soap:Envelope>";
        }
    }
}
