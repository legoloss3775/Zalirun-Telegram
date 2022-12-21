using System;
using System.Collections.Generic;
using System.Text;

namespace Zalirun.Telegram
{
    public class MessageDeleteEventArgs : EventArgs
    {
        public string ChatId { get; set; }
        public int MessageId { get; set; }

        public MessageDeleteEventArgs(string chatId, int messageId)
        {
            ChatId = chatId;
            MessageId = messageId;
        }
    }
}
