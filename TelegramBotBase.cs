using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using global::Telegram.Bot;
using global::Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;
using Telegram.Bot.Types.ReplyMarkups;
using Zalirun.Extentions;

namespace Zalirun.Telegram.Core
{
    public abstract class TelegramBotBase<T> : ITelegramBot where T : IMessageArgs
    {
        public static ITelegramBotClient Client { get; private set; }

        protected static readonly ILogger Logger = NLog.LogManager.GetCurrentClassLogger();
        protected static readonly SemaphoreSlim FileReaderSemaphore = new SemaphoreSlim(1, 1);
        protected static System.Timers.Timer ClientRestartTimer { get; private set; }
        protected static CancellationTokenSource ClientCancellationTokenSource { get; private set; }

        public abstract ITelegramBotConfigurator TelegramBotConfigurator { get; }
        public abstract string TelegramBotName { get; }
        public virtual string SentMessagesFileName => $"{this.GetType().Name}.json";
        public virtual Dictionary<string, T> SentMessages { get; private set; } = new Dictionary<string, T>();
        public virtual Dictionary<Guid, T> MessageTimerIds { get; private set; } = new Dictionary<Guid, T>();

        protected virtual int DeleteOldMessagesDaysInterval => 30;

        public static event EventHandler<Update> UpdateRecieved;
        public static event EventHandler<Exception> ExceptionRecieved;

        public virtual event EventHandler ClientStart;
        public virtual event EventHandler Initialized;
        public virtual event EventHandler<MessageTimerEventArgs> TimedMessageCreated;
        public virtual event EventHandler<IMessageArgs> MessageSent;
        public virtual event EventHandler<IMessageArgs> MessageEdit;
        public virtual event EventHandler<MessageDeleteEventArgs> MessageDelete;

        public TelegramBotBase()
        {
            Init();
        }

        public void Init()
        {
            try
            {
                Logger.Info($"Begin >> Init {GetType().Name}");

                if (TelegramBotConfigurator == null)
                {
                    throw new Exception("Telegram Configurator not found");
                }

                LoadSentMessagesFromDataStore();
                if (Client == null)
                {
                    StartClient(TelegramBotConfigurator.GetTelegramBotToken(TelegramBotName));
                    SetClientRestartTimer(this);
                }

                OnInit(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                Logger.Info($"End >> Init {GetType().Name}");
            }
        }

        public void StartClient(string token)
        {
            try
            {
                Logger.Info($"Begin >> Start Client {GetType().Name}");
                if (Client != null && ClientCancellationTokenSource != null && ClientCancellationTokenSource.IsCancellationRequested == false)
                {
                    Logger.Error(new Exception("Telegram Client already started"));
                    return;
                }

                Client = new TelegramBotClient(token);

                ClientCancellationTokenSource = new System.Threading.CancellationTokenSource();
                var cancellationToken = ClientCancellationTokenSource.Token;
                var receiverOptions = new global::Telegram.Bot.Polling.ReceiverOptions
                {
                    AllowedUpdates = { }, // receive all update types
                };

                Client.StartReceiving(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    receiverOptions,
                    cancellationToken
                );

                UpdateRecieved += OnUpdateRecieved;
                ExceptionRecieved += OnExceptionRecieved;

                OnClientStart(this);

                Logger.Info($"End >> Start Client {GetType().Name}");
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public async Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                Logger.Error(exception, "Telegram update exception");

                ClientCancellationTokenSource.Cancel();
                ClientCancellationTokenSource.Dispose();

                StartClient(TelegramBotConfigurator.GetTelegramBotToken(TelegramBotName));

                ExceptionRecieved?.Invoke(this, exception);
            });
        }

        public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
        {
            await Task.Yield();

            Logger.Info($"Telegram Update : {update.Type} {update.CallbackQuery?.From} {update.CallbackQuery?.Data}");
            Logger.Trace($"{Newtonsoft.Json.JsonConvert.SerializeObject(update, Newtonsoft.Json.Formatting.Indented)}");

            UpdateRecieved?.Invoke(this, update);
        }

        public void ClearAllTimers()
        {
            TimerManager.ClearTimers(MessageTimerIds.Keys.ToList());
            MessageTimerIds.Clear();
        }

        public void ClearTimers(List<Guid> timerIds)
        {
            TimerManager.ClearTimers(timerIds);
            foreach (var id in timerIds)
            {
                if (!MessageTimerIds.ContainsKey(id))
                {
                    Logger.Warn($"Timer not found: id - {id}");
                    continue;
                }
                else
                {
                    MessageTimerIds.Remove(id);
                }
            }
        }

        public virtual Task<System.Timers.Timer> SendTimedMessageAsync(
            double interval,
            bool autoReset,
            string chatId,
            string messageText,
            IMessageArgs args,
            IReplyMarkup replyMarkup = null,
            ParseMode? parseMode = null,
            IEnumerable<MessageEntity> messageEntities = null,
            bool? disableWebPagePrievew = null,
            bool? disableNotification = null,
            bool? protectContent = null,
            int? replyToMessageId = null,
            bool? allowSendingWithoutReply = null,
            CancellationToken cancellationToken = default,
            Action<Message> onTimedEvent = null)
        {
            var timer = TimerManager.SetTimer(interval, autoReset, out var timerId);
            args.ChatId = chatId;
            if (args is T tArgs)
            {
                MessageTimerIds.Add(timerId, tArgs);
            }

            timer.Elapsed += async (sender, e) =>
            {
                var message = await SendMessageAsync(chatId, messageText, args, replyMarkup, parseMode, messageEntities, disableWebPagePrievew, disableNotification,
                                                     protectContent, replyToMessageId, allowSendingWithoutReply, cancellationToken);

                if (replyMarkup != null)
                {
                    LogReplyMarkup(message, replyMarkup);
                }
                if (!autoReset)
                {
                    MessageTimerIds.Remove(timerId);
                }

                onTimedEvent?.Invoke(message);
            };

            OnTimedMessageCreated(this, new MessageTimerEventArgs(timerId, args));

            Logger.Trace($"Created message with timer Id : {timerId} for chatId : {chatId}\nMessage :\n{{\n{messageText}\n}}");
            Logger.Info($"Created message with timer Id : {timerId}, chatId : {chatId}");
            return Task.FromResult(timer);
        }

        public virtual async Task<Message> SendMessageAsync(string chatId,
            string messageText,
            IMessageArgs args,
            IReplyMarkup replyMarkup = null,
            ParseMode? parseMode = null,
            IEnumerable<MessageEntity> messageEntities = null,
            bool? disableWebPagePrievew = null,
            bool? disableNotification = null,
            bool? protectContent = null,
            int? replyToMessageId = null,
            bool? allowSendingWithoutReply = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var message = await Client.SendTextMessageAsync(chatId, messageText, parseMode, messageEntities, disableWebPagePrievew, disableNotification,
                                                                protectContent, replyToMessageId, allowSendingWithoutReply, replyMarkup, cancellationToken);

                if (replyMarkup != null)
                {
                    LogReplyMarkup(message, replyMarkup);
                }
                args.Message = message;

                if (args is T tArgs)
                {
                    var messageId = tArgs.Message.MessageId.ToString();
                    await AddSentMessageAsync(tArgs);
                }

                OnMessageSent(this, args);

                Logger.Trace($"Sent message Id: {message.MessageId}\n{{\n{message.Text}\n}}");
                Logger.Info($"Sent message Id: {message.MessageId}");
                return message;
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return default;
            }
        }

        public virtual async Task EditTextMessageAsync(
            Message message,
            IMessageArgs args,
            ParseMode? parseMode = null,
            bool? disableWebPreview = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var sentMessage = await Client.EditMessageTextAsync(
                    message.Chat.Id,
                    message.MessageId,
                    message.Text,
                    parseMode,
                    message.Entities,
                    disableWebPreview,
                    message.ReplyMarkup,
                    cancellationToken);

                args.Message = message;

                if (args is T tArgs)
                {
                    await EditSentMessageAsync(tArgs);
                }
                OnMessageEdit(this, args);

                Logger.Trace($"Edited message Id: {message.MessageId}\n{{\n{message.Text}\n}}");
                Logger.Info($"Edited message Id: {message.MessageId}");
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public virtual async Task<bool> DeleteMessage(string chatId, int messageId)
        {
            try
            {
                await Client.DeleteMessageAsync(chatId, messageId);
                if (SentMessages.ContainsKey(messageId.ToString()))
                {
                    await RemoveSentMessageAsync(messageId);
                }
                OnMessageDelete(this, new MessageDeleteEventArgs(chatId, messageId));

                Logger.Info($"Deleted message Id: {messageId} in chat {chatId}");
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }
        protected static void SetClientRestartTimer(TelegramBotBase<T> bot)
        {
            var token = bot.TelegramBotConfigurator.GetTelegramBotToken(bot.TelegramBotName);

            ClientRestartTimer = TimerManager.SetTimer(bot.TelegramBotConfigurator.ClientRestartInterval, true, out _);
            ClientRestartTimer.Elapsed += async (sender, e) =>
            {
                if (Client == null)
                {
                    bot.StartClient(token);
                }
                else
                {
                    try
                    {
                        var user = await Client.GetMeAsync();
                        if (user == null)
                        {
                            ClientCancellationTokenSource.Cancel();
                            bot.StartClient(token);
                        }
                    }
                    catch (Exception)
                    {
                        ClientCancellationTokenSource.Cancel();
                        bot.StartClient(token);
                    }
                }
            };
        }

        protected virtual void OnInit(object sender, EventArgs e)
        {
            Initialized?.Invoke(sender, e);
        }

        protected virtual void OnClientStart(object sender)
        {
            ClientStart?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnTimedMessageCreated(object sender, MessageTimerEventArgs e)
        {
            TimedMessageCreated?.Invoke(sender, e);
        }

        protected virtual void OnMessageSent(object sender, IMessageArgs e)
        {
            MessageSent?.Invoke(sender, e);
        }

        protected virtual void OnMessageEdit(object sender, IMessageArgs e)
        {
            MessageEdit?.Invoke(sender, e);
        }

        protected virtual void OnMessageDelete(object sender, MessageDeleteEventArgs e)
        {
            MessageDelete?.Invoke(sender, e);
        }

        protected virtual async Task RemoveSentMessageAsync(int messageId)
        {
            await FileReaderSemaphore.WaitAsync();

            if (SentMessages.ContainsKey(messageId.ToString()))
            {
                SentMessages.Remove(messageId.ToString());
                _ = Task.Run(() =>
                {
                    FileManager.WriteJson(SentMessagesFileName, SentMessages);
                    FileReaderSemaphore.Release();
                });
            }
        }

        protected virtual async Task AddSentMessageAsync(T args)
        {
            await FileReaderSemaphore.WaitAsync();

            SentMessages.Add(args?.Message.MessageId.ToString(), args);
            _ = Task.Run(() =>
            {
                FileManager.WriteJson(SentMessagesFileName, SentMessages);
                FileReaderSemaphore.Release();
            });
        }

        protected virtual async Task EditSentMessageAsync(T args)
        {
            await FileReaderSemaphore.WaitAsync();

            var messageId = args.Message.MessageId.ToString();
            if (SentMessages.ContainsKey(messageId))
            {
                SentMessages[messageId] = args;
                _ = Task.Run(() =>
                {
                    FileManager.WriteJson(SentMessagesFileName, SentMessages);
                    FileReaderSemaphore.Release();
                });
            }
        }

        protected virtual void LoadSentMessagesFromDataStore()
        {
            var dictionary = FileManager.ReadJson<Dictionary<string, T>>(SentMessagesFileName);
            if (dictionary != null)
            {
                SentMessages = dictionary;
            }
            else
            {
                dictionary = new Dictionary<string, T>();
            }

            Logger.Info($"{SentMessagesFileName} - found {dictionary.Count} values");
        }

        protected virtual void DeleteOldMessagesFromDataStore()
        {
            var count = 0;
            foreach (var keyValue in SentMessages.ToDictionary(x => x.Key, y => y.Value))
            {
                var message = keyValue.Value;
                if ((DateTime.UtcNow - message.Message.Date).Days > DeleteOldMessagesDaysInterval)
                {
                    SentMessages.Remove(keyValue.Key);
                    count++;
                }
            }

            Logger.Info($"{SentMessagesFileName} - deleted {count} values");

            FileManager.WriteJson(SentMessagesFileName, SentMessages);
        }

        protected abstract Task HandleUnkownUpdateAsync(Update e);

        protected abstract Task HandleMessageUpdateAsync(Message message);

        protected abstract Task HandleInlineQueryUpdateAsync(InlineQuery inlineQuery);

        protected abstract Task HandleChosenInlineResultUpdateAsync(ChosenInlineResult chosenInlineResult);

        protected abstract Task HandleCallbackQueryUpdateAsync(CallbackQuery callbackQuery);

        protected abstract Task HandleEditedMessageUpdateAsync(Message editedMessage);

        protected abstract Task HandleChannelPostUpdateAsync(Message channelPost);

        protected abstract Task HandleEditChannelPostUpdateAsync(Message editedChannelPost);

        protected abstract Task HandleShippingQueryUpdateAsync(ShippingQuery shippingQuery);

        protected abstract Task HandlePreCheckoutQueryUpdateAsync(PreCheckoutQuery preCheckoutQuery);

        protected abstract Task HandlePollUpdateAsync(Poll poll);

        protected abstract Task HandlePollAnswerUpdateAsync(PollAnswer pollAnswer);

        protected abstract Task HandleMyChatMemberUpdateAsync(ChatMemberUpdated myChatMember);

        protected abstract Task HandleChatMemberUpdateAsync(ChatMemberUpdated chatMember);

        protected abstract Task HandleChatJoinRequestUpdateAsync(ChatJoinRequest chatJoinRequest);

        protected abstract void OnExceptionRecieved(object sender, Exception e);

        private void OnUpdateRecieved(object sender, Update e)
        {
            try
            {
                switch (e.Type)
                {
                    case UpdateType.Unknown:
                        HandleUnkownUpdateAsync(e);
                        break;
                    case UpdateType.Message:
                        HandleMessageUpdateAsync(e.Message);
                        break;
                    case UpdateType.InlineQuery:
                        HandleInlineQueryUpdateAsync(e.InlineQuery);
                        break;
                    case UpdateType.ChosenInlineResult:
                        HandleChosenInlineResultUpdateAsync(e.ChosenInlineResult);
                        break;
                    case UpdateType.CallbackQuery:
                        HandleCallbackQueryUpdateAsync(e.CallbackQuery);
                        break;
                    case UpdateType.EditedMessage:
                        HandleEditedMessageUpdateAsync(e.EditedMessage);
                        break;
                    case UpdateType.ChannelPost:
                        HandleChannelPostUpdateAsync(e.ChannelPost);
                        break;
                    case UpdateType.EditedChannelPost:
                        HandleEditChannelPostUpdateAsync(e.EditedChannelPost);
                        break;
                    case UpdateType.ShippingQuery:
                        HandleShippingQueryUpdateAsync(e.ShippingQuery);
                        break;
                    case UpdateType.PreCheckoutQuery:
                        HandlePreCheckoutQueryUpdateAsync(e.PreCheckoutQuery);
                        break;
                    case UpdateType.Poll:
                        HandlePollUpdateAsync(e.Poll);
                        break;
                    case UpdateType.PollAnswer:
                        HandlePollAnswerUpdateAsync(e.PollAnswer);
                        break;
                    case UpdateType.MyChatMember:
                        HandleMyChatMemberUpdateAsync(e.MyChatMember);
                        break;
                    case UpdateType.ChatMember:
                        HandleChatMemberUpdateAsync(e.ChatMember);
                        break;
                    case UpdateType.ChatJoinRequest:
                        HandleChatJoinRequestUpdateAsync(e.ChatJoinRequest);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        protected static void LogReplyMarkup(Message message, IReplyMarkup replyMarkup)
        {
            if (message == null)
            {
                Logger.Error($"LogReplyMarkup >> Message was null");
            }
            if (replyMarkup == null)
            {
                Logger.Error($"LogReplyMarkup >> ReplyMarkup was null");
            }
            if (message == null || replyMarkup == null)
            {
                return;
            }
            if (replyMarkup is InlineKeyboardMarkup inlineKeyboardMarkup)
            {
                var sb = new StringBuilder();
                foreach (var row in inlineKeyboardMarkup.InlineKeyboard)
                {
                    foreach (var button in row)
                        sb.Append($"{button.Text} ");
                    sb.AppendLine();
                }

                Logger.Trace($"Sent inline keyboard message Id: {message.MessageId} \n{message.Text} \n Keyboard : {sb.ToString()}");
                Logger.Info($"Sent inline keyboard message Id: {message.MessageId}");
            }
        }
    }
}
