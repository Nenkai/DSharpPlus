using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity.Concurrency;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DSharpPlus.Exceptions;

namespace DSharpPlus.Interactivity.EventHandling
{
    /// <summary>
    /// Eventwaiter is a class that serves as a layer between the InteractivityExtension
    /// and the DiscordClient to listen to an event and check for matches to a predicate.
    /// </summary>
    internal class ReactionListener : IDisposable
    {
        DiscordClient _client;

        AsyncEvent<MessageReactionAddEventArgs> _reactionAddEvent;
        AsyncEventHandler<MessageReactionAddEventArgs> _reactionAddHandler;

        AsyncEvent<MessageReactionRemoveEventArgs> _reactionRemoveEvent;
        AsyncEventHandler<MessageReactionRemoveEventArgs> _reactionRemoveHandler;

        ConcurrentHashSet<ReactionListenerRequest> _requests;

        /// <summary>
        /// Creates a new Eventwaiter object.
        /// </summary>
        /// <param name="client">Your DiscordClient</param>
        public ReactionListener(DiscordClient client)
        {
            this._client = client;
            var tinfo = _client.GetType().GetTypeInfo();

            _requests = new ConcurrentHashSet<ReactionListenerRequest>();

            // Grabbing all three events from client
            var handler = tinfo.DeclaredFields.First(x => x.FieldType == typeof(AsyncEvent<MessageReactionAddEventArgs>));

            this._reactionAddEvent = (AsyncEvent<MessageReactionAddEventArgs>)handler.GetValue(_client);
            this._reactionAddHandler = new AsyncEventHandler<MessageReactionAddEventArgs>(HandleReactionAdd);
            this._reactionAddEvent.Register(_reactionAddHandler);

            handler = tinfo.DeclaredFields.First(x => x.FieldType == typeof(AsyncEvent<MessageReactionRemoveEventArgs>));

            this._reactionRemoveEvent = (AsyncEvent<MessageReactionRemoveEventArgs>)handler.GetValue(_client);
            this._reactionRemoveHandler = new AsyncEventHandler<MessageReactionRemoveEventArgs>(HandleReactionRemove);
            this._reactionRemoveEvent.Register(_reactionRemoveHandler);
        }

        public async Task<DiscordEmoji> GetFirstReactionEmojiAsync(ReactionListenerRequest req)
        {
            this._requests.Add(req);
            var result = (DiscordEmoji)null;

            try
            {
                await req._tcs.Task;
            }
            catch (Exception ex)
            {
                this._client.DebugLogger.LogMessage(LogLevel.Error, "Interactivity",
                    $"Something went wrong collecting first reaction with exception {ex.GetType().Name}.", DateTime.Now);
            }
            finally
            {
                result = req._collected;
                req.Dispose();
                this._requests.TryRemove(req);
            }
            return result;
        }

        async Task HandleReactionAdd(MessageReactionAddEventArgs eventargs)
        {
            await Task.Yield();
            // foreach request add
            foreach(var req in _requests)
            {
                if (req.message.Id == eventargs.Message.Id && req.user == eventargs.User)
                {
                    req._collected = eventargs.Emoji;
                    req._tcs.TrySetResult(req._collected);
                }
            }
        }

        async Task HandleReactionRemove(MessageReactionRemoveEventArgs eventargs)
        {
            await Task.Yield();
            // foreach request remove
            foreach (var req in _requests)
            {
                if (req.message.Id == eventargs.Message.Id && req.user == eventargs.User)
                {
                    req._collected = eventargs.Emoji;
                    req._tcs.TrySetResult(req._collected);
                }
            }
        }


        ~ReactionListener()
        {
            this.Dispose();
        }

        /// <summary>
        /// Disposes this EventWaiter
        /// </summary>
        public void Dispose()
        {
            this._client = null;

            this._reactionAddEvent.Unregister(this._reactionAddHandler);
            this._reactionRemoveEvent.Unregister(this._reactionRemoveHandler);

            this._reactionAddEvent = null;
            this._reactionAddHandler = null;
            this._reactionRemoveEvent = null;
            this._reactionRemoveHandler = null;

            _requests.Clear();
            _requests = null;
        }
    }

    public class ReactionListenerRequest : IDisposable
    {
        internal TaskCompletionSource<DiscordEmoji> _tcs;
        internal CancellationTokenSource _ct;
        internal TimeSpan _timeout;
        internal DiscordMessage message;
        internal DiscordUser user;
        internal DiscordEmoji _collected;

        public ReactionListenerRequest(DiscordMessage msg, DiscordUser u, TimeSpan timeout)
        {
            message = msg;
            user = u;
            _timeout = timeout;
            _tcs = new TaskCompletionSource<DiscordEmoji>();
            _ct = new CancellationTokenSource(_timeout);
            _ct.Token.Register(() => _tcs.TrySetResult(null));
        }

        ~ReactionListenerRequest()
        {
            this.Dispose();
        }

        public void Dispose()
        {
            this._ct.Dispose();
            this._tcs = null;
            this.message = null;
            this.user = null;
            this._collected = null;
        }
    }
}
