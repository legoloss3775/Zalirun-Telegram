using System;
using System.Collections.Generic;
using System.Text;

namespace Zalirun.Telegram.Core
{
    public interface ITelegramBotConfigurator
    {
        string GetTelegramBotToken(string botName);
    }
}
