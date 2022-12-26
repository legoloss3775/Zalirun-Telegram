using System;
using System.Collections.Generic;
using System.Text;

namespace Zalirun.Telegram.Core
{
    public interface ITelegramBotConfigurator
    {
        double ClientRestartInterval { get; }
        double OldMessagesDeleteInterval { get; }

        string GetTelegramBotToken(string botName);
    }
}
