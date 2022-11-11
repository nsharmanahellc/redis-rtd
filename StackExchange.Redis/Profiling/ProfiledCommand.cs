﻿using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StackExchange.Redis.Profiling
{
    internal sealed class ProfiledCommand : IProfiledCommand
    {
        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

        public EndPoint EndPoint => Server.EndPoint;

        public int Db => Message!.Db;

        public string Command => Message is RedisDatabase.ExecuteMessage em ? em.Command.ToString() : Message!.Command.ToString();

        public CommandFlags Flags => Message!.Flags;

        public DateTime CommandCreated { get; private set; }

        public TimeSpan CreationToEnqueued => GetElapsedTime(EnqueuedTimeStamp - MessageCreatedTimeStamp);

        public TimeSpan EnqueuedToSending => GetElapsedTime(RequestSentTimeStamp - EnqueuedTimeStamp);

        public TimeSpan SentToResponse => GetElapsedTime(ResponseReceivedTimeStamp - RequestSentTimeStamp);

        public TimeSpan ResponseToCompletion => GetElapsedTime(CompletedTimeStamp - ResponseReceivedTimeStamp);

        public TimeSpan ElapsedTime => GetElapsedTime(CompletedTimeStamp - MessageCreatedTimeStamp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TimeSpan GetElapsedTime(long timestampDelta)
        {
            return new TimeSpan((long)(TimestampToTicks * timestampDelta));
        }

        public IProfiledCommand? RetransmissionOf => OriginalProfiling;

        public RetransmissionReasonType? RetransmissionReason { get; }

        public ProfiledCommand? NextElement { get; set; }

        private Message? Message;
        private readonly ServerEndPoint Server;
        private readonly ProfiledCommand? OriginalProfiling;
        private long MessageCreatedTimeStamp;
        private long EnqueuedTimeStamp;
        private long RequestSentTimeStamp;
        private long ResponseReceivedTimeStamp;
        private long CompletedTimeStamp;
        private ConnectionType? ConnectionType;

        private readonly ProfilingSession PushToWhenFinished;

        private ProfiledCommand(ProfilingSession pushTo, ServerEndPoint server, ProfiledCommand? resentFor, RetransmissionReasonType? reason)
        {
            PushToWhenFinished = pushTo;
            OriginalProfiling = resentFor;
            Server = server;
            RetransmissionReason = reason;
        }

        public static ProfiledCommand NewWithContext(ProfilingSession pushTo, ServerEndPoint server)
        {
            return new ProfiledCommand(pushTo, server, null, null);
        }

        public static ProfiledCommand NewAttachedToSameContext(ProfiledCommand resentFor, ServerEndPoint server, bool isMoved)
        {
            return new ProfiledCommand(resentFor.PushToWhenFinished, server, resentFor, isMoved ? RetransmissionReasonType.Moved : RetransmissionReasonType.Ask);
        }

        [MemberNotNull(nameof(Message))]
        public void SetMessage(Message msg)
        {
            // This method should never be called twice
            if (Message is not null)
            {
                throw new InvalidOperationException($"{nameof(SetMessage)} called more than once");
            }

            Message = msg;
            CommandCreated = msg.CreatedDateTime;
            MessageCreatedTimeStamp = msg.CreatedTimestamp;
        }

        public void SetEnqueued(ConnectionType? connType)
        {
            SetTimestamp(ref EnqueuedTimeStamp);
            ConnectionType = connType;
        }

        public void SetRequestSent() => SetTimestamp(ref RequestSentTimeStamp);

        public void SetResponseReceived() => SetTimestamp(ref ResponseReceivedTimeStamp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetTimestamp(ref long field)
        {
            var now = Stopwatch.GetTimestamp();
            Interlocked.CompareExchange(ref field, now, 0);
        }

        public void SetCompleted()
        {
            // this method can be called multiple times, depending on how the task completed (async vs not)
            //   so we actually have to guard against it.

            var now = Stopwatch.GetTimestamp();
            var oldVal = Interlocked.CompareExchange(ref CompletedTimeStamp, now, 0);

            // only push on the first call, no dupes!
            if (oldVal == 0)
            {
                // fake a response if we completed prematurely (timeout, broken connection, etc)
                Interlocked.CompareExchange(ref ResponseReceivedTimeStamp, now, 0);
                PushToWhenFinished?.Add(this);
            }
        }

        public override string ToString() =>
$@"{Command} (DB: {Db}, Flags: {Flags})
     EndPoint = {EndPoint} ({ConnectionType})
     Created = {CommandCreated:HH:mm:ss.ffff}
     ElapsedTime = {ElapsedTime.TotalMilliseconds} ms (CreationToEnqueued: {CreationToEnqueued.TotalMilliseconds} ms, EnqueuedToSending: {EnqueuedToSending.TotalMilliseconds} ms, SentToResponse: {SentToResponse.TotalMilliseconds} ms, ResponseToCompletion = {ResponseToCompletion.TotalMilliseconds} ms){(RetransmissionOf != null ? @"
     RetransmissionOf = " + RetransmissionOf : "")}";
    }
}