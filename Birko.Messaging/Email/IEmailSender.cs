using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Email;

public interface IEmailSender : IMessageSender<EmailMessage>
{
    Task<MessageResult> SendAsync(
        MessageAddress from,
        MessageAddress to,
        string subject,
        string body,
        bool isHtml = false,
        CancellationToken ct = default);
}
