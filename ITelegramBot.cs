using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::Telegram.Bot;
using global::Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Zalirun.Telegram.Core
{
    public interface ITelegramBot
    {
        event EventHandler<IMessageArgs> MessageSent;
        event EventHandler<IMessageArgs> MessageEdit;
        event EventHandler<MessageDeleteEventArgs> MessageDelete;

        void StartClient(string token);

        Task<Message> SendMessageAsync(string chatId, string messageText, IMessageArgs args,
                               IReplyMarkup replyMarkup = null, ParseMode? parseMode = null,
                               IEnumerable<MessageEntity> messageEntities = null, bool? disableWebPagePrievew = null,
                               bool? disableNotification = null, bool? protectContent = null, int? replyToMessageId = null,
                               bool? allowSendingWithoutReply = null, CancellationToken cancellationToken = default);

        Task EditTextMessageAsync(Message message, IMessageArgs args,
                                 ParseMode? parseMode = null,
                                 bool? disableWebPreview = null,
                                 CancellationToken cancellationToken = default);

        Task<bool> DeleteMessage(string chatId, int messageId);

        Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, System.Threading.CancellationToken cancellationToken);

        Task HandleUpdateAsync(ITelegramBotClient bot, Update update, System.Threading.CancellationToken cancellationToken);
    }
}