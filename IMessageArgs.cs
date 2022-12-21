using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot.Types;

namespace Zalirun.Telegram.Core
{
    public interface IMessageArgs
    {
        Message Message { get; set; }
    }
}
