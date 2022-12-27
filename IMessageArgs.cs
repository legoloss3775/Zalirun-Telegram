using System;
using System.Collections.Generic;
using System.Text;
using global::Telegram.Bot.Types;

namespace Zalirun.Telegram.Core
{
    public interface IMessageArgs
    {
        string ChatId { get; set; }
        Message Message { get; set; }
    }
}
