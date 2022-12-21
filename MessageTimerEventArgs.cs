using System;
using System.Collections.Generic;
using System.Text;
using Zalirun.Telegram.Core;

namespace Zalirun.Telegram
{
    public class MessageTimerEventArgs : EventArgs
    {

        public Guid TimerId { get; set; }
        public IMessageArgs MessageArgs { get; set; }

        public MessageTimerEventArgs(Guid timerId, IMessageArgs messageArgs)
        {
            TimerId = timerId;
            MessageArgs = messageArgs;
        }
    }
}
