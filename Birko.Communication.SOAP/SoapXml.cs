namespace Birko.Communication.SOAP
{
    /// <summary>
    /// Shared SOAP XML helpers. The escape logic and the SOAP-fault envelope template were duplicated
    /// across SoapServer and SoapAuthenticationService and could drift independently (CR-L086).
    /// </summary>
    internal static class SoapXml
    {
        /// <summary>Escapes the five XML special characters in element text.</summary>
        public static string Escape(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        /// <summary>Builds a SOAP 1.1 fault envelope with the given fault code and (escaped) message.</summary>
        public static string BuildFault(string code, string message)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <soap:Fault>
      <faultcode>soap:{code}</faultcode>
      <faultstring>{Escape(message)}</faultstring>
    </soap:Fault>
  </soap:Body>
</soap:Envelope>";
        }
    }
}
