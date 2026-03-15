using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Templates;

public interface ITemplateEngine
{
    Task<string> RenderAsync(string template, object model, CancellationToken ct = default);

    Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, CancellationToken ct = default);
}
