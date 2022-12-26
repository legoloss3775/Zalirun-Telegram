using System;
using System.Collections.Generic;
using System.Text;
using global::Telegram.Bot.Types;

namespace Zalirun.Telegram.Core
{
    public interface IMessageArgs
    {
        Message Message { get; set; }
    }
}
