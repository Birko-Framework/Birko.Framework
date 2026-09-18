using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Messaging.Email;

public class EmailSettings : RemoteSettings, Birko.Data.Models.ILoadable<EmailSettings>
{
    public int Timeout { get; set; } = 30000;
    public MessageAddress? DefaultFrom { get; set; }

    public EmailSettings() : base()
    {
        UseSecure = true;
    }

    public EmailSettings(string host, int port, string? username = null, string? password = null)
    {
        Location = host;
        Port = port;
        UseSecure = true;
        if (username != null)
        {
            UserName = username;
        }
        if (password != null)
        {
            Password = password;
        }
    }

    public void LoadFrom(EmailSettings data)
    {
        base.LoadFrom(data);
        if (data != null)
        {
            Timeout = data.Timeout;
            DefaultFrom = data.DefaultFrom;
        }
    }

    public override void LoadFrom(Settings data)
    {
        if (data is EmailSettings emailData)
        {
            LoadFrom(emailData);
        }
    }
}
