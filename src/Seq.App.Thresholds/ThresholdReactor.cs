﻿using System;
using Seq.Apps;
using Seq.Apps.LogEvents;

namespace Seq.App.Thresholds
{
    /// <summary>
    /// Counts events in a sliding time window, writing a message back to the
    /// stream when a set threshold is reached.
    /// </summary>
    /// 
    [SeqApp("Threshold Detection",
        Description = "Counts events in a sliding time window, writing an event back to the stream when a set threshold is reached.")]
    public class ThresholdReactor : Reactor, ISubscribeTo<LogEventData>
    {
        // Each bucket counts events in a one-second interval
        int[] _buckets;

        // A running sum of all buckets
        int _sum;

        // The index of the current ('latest') bucket
        int _currentBucket;

        // The date/time, rounded down to the second, corresponding
        // with the latest bucket.
        DateTime _currentBucketSecond;

        // The time to suppress messages after the threshold has been crossed
        TimeSpan _suppressionTime;

        // Tracks when suppression is due until
        DateTime? _suppressUntilUtc;

        [SeqAppSetting(
            DisplayName = "Detection window (seconds)",
            HelpText = "The number of seconds within which the count must exceed the threshold to trigger the event.")]
        public int WindowSeconds { get; set; }

        [SeqAppSetting(
            DisplayName = "Threshold (count)",
            HelpText = "The number of events that will trigger the threshold.")]
        public int EventsInWindowThreshold { get; set; }

        [SeqAppSetting(
            DisplayName = "Suppression time (seconds)",
            IsOptional = true,
            HelpText = "Once the threshold is reached, the time to wait before checking again. The default is zero.")]
        public int SuppressionSeconds { get; set; }

        [SeqAppSetting(
            DisplayName = "Threshold name",
            HelpText = "The name of this threshold; the events written back to the stream will be tagged with this value.")]
        public string ThresholdName { get; set; }

        // Sets up the sliding buffer when the app starts
        protected override void OnAttached()
        {
            base.OnAttached();

            _buckets = new int[WindowSeconds];
            _currentBucketSecond = DateTime.UtcNow;
            _currentBucket = 0;
            _suppressionTime = TimeSpan.FromSeconds(SuppressionSeconds);
        }

        // Called by Seq whenever an event is send to the app
        public void On(Event<LogEventData> evt)
        {
            int eventBucket;
            if (!TrySlideWindow(evt, out eventBucket))
                return;

            _buckets[eventBucket]++;
            _sum++;

            if (IsSuppressed())
                return;

            if (_sum < EventsInWindowThreshold)
                return;

            _suppressUntilUtc = DateTime.UtcNow + _suppressionTime;
            Log.Information("Threshold {ThresholdName} reached: {EventCount} events observed within {WindowSize} sec. (message suppressed for {SuppressionSeconds} sec.)",
                ThresholdName, _sum, _buckets.Length, (int)_suppressionTime.TotalSeconds);
        }

        // Adjusts the window to fit the event, providing the index into
        // the buckets array that the event belongs to; if the event is
        // too late to fit the window, returns false
        bool TrySlideWindow(Event<LogEventData> evt, out int eventBucket)
        {
            var eventSeconds = SecondsFloor(evt.TimestampUtc);
            var distance = (int)(eventSeconds - _currentBucketSecond).TotalSeconds;

            if (distance < 0)
            {
                if (distance <= -_buckets.Length)
                {
                    eventBucket = 0;
                    return false;
                }

                eventBucket = (_currentBucket + distance) % _buckets.Length;
                return true;
            }

            if (distance > 0)
            {
                var newCurrent = (_currentBucket + distance) % _buckets.Length;
                var firstReused = (_currentBucket + 1) % _buckets.Length;
                if (distance >= _buckets.Length)
                {
                    _sum = 0;
                    for (var i = 0; i < _buckets.Length; i++)
                    {
                        _buckets[i] = 0;
                    }
                }
                else if (newCurrent >= firstReused)
                {
                    for (var i = firstReused; i <= newCurrent; i++)
                    {
                        _sum -= _buckets[i];
                        _buckets[i] = 0;
                    }
                }
                else
                {
                    for (var i = firstReused; i < _buckets.Length; i++)
                    {
                        _sum -= _buckets[i];
                        _buckets[i] = 0;
                    }

                    for (var i = 0; i <= newCurrent; i++)
                    {
                        _sum -= _buckets[i];
                        _buckets[i] = 0;
                    }
                }

                _currentBucket = newCurrent;
                _currentBucketSecond = eventSeconds;
            }

            eventBucket = _currentBucket;
            return true;
        }

        // Check the current suppression time
        bool IsSuppressed()
        {
            return _suppressUntilUtc.HasValue && _suppressUntilUtc.Value > DateTime.UtcNow;
        }

        // Returns the provided date time rounded down to the nearest second
        static DateTime SecondsFloor(DateTime dateTime)
        {
            return dateTime - TimeSpan.FromTicks(dateTime.Ticks % TimeSpan.TicksPerSecond);
        }
    }
}
