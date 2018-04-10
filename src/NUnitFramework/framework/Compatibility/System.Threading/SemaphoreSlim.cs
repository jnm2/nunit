// https://raw.githubusercontent.com/dotnet/coreclr/v2.0.6/src/mscorlib/src/System/Threading/SemaphoreSlim.cs
#if NET20 || NET35

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// =+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
//
//
// A lightweight semahore class that contains the basic semaphore functions plus some useful functions like interrupt
// and wait handle exposing to allow waiting on multiple semaphores.
//
// =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Runtime.InteropServices;

// The class will be part of the current System.Threading namespace

namespace System.Threading
{
    /// <summary>
    /// Limits the number of threads that can access a resource or pool of resources concurrently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="SemaphoreSlim"/> provides a lightweight semaphore class that doesn't
    /// use Windows kernel semaphores.
    /// </para>
    /// <para>
    /// All public and protected members of <see cref="SemaphoreSlim"/> are thread-safe and may be used
    /// concurrently from multiple threads, with the exception of Dispose, which
    /// must only be used when all other operations on the <see cref="SemaphoreSlim"/> have
    /// completed.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("Current Count = {m_currentCount}")]
    public class SemaphoreSlim : IDisposable
    {
        #region Private Fields

        // The semaphore count, initialized in the constructor to the initial value, every release call incremetns it
        // and every wait call decrements it as long as its value is positive otherwise the wait will block.
        // Its value must be between the maximum semaphore value and zero
        private volatile int m_currentCount;

        // The maximum semaphore value, it is initialized to Int.MaxValue if the client didn't specify it. it is used
        // to check if the count excceeded the maxi value or not.
        private readonly int m_maxCount;

        // The number of synchronously waiting threads, it is set to zero in the constructor and increments before blocking the
        // threading and decrements it back after that. It is used as flag for the release call to know if there are
        // waiting threads in the monitor or not.
        private volatile int m_waitCount;

        // Dummy object used to in lock statements to protect the semaphore count, wait handle and cancelation
        private object m_lockObj;

        // Act as the semaphore wait handle, it's lazily initialized if needed, the first WaitHandle call initialize it
        // and wait an release sets and resets it respectively as long as it is not null
        private volatile ManualResetEvent m_waitHandle;

        // No maximum constant
        private const int NO_MAXIMUM = Int32.MaxValue;
        #endregion

        #region Public properties

        /// <summary>
        /// Gets the current count of the <see cref="SemaphoreSlim"/>.
        /// </summary>
        /// <value>The current count of the <see cref="SemaphoreSlim"/>.</value>
        public int CurrentCount
        {
            get { return m_currentCount; }
        }

        /// <summary>
        /// Returns a <see cref="T:System.Threading.WaitHandle"/> that can be used to wait on the semaphore.
        /// </summary>
        /// <value>A <see cref="T:System.Threading.WaitHandle"/> that can be used to wait on the
        /// semaphore.</value>
        /// <remarks>
        /// A successful wait on the <see cref="AvailableWaitHandle"/> does not imply a successful wait on
        /// the <see cref="SemaphoreSlim"/> itself, nor does it decrement the semaphore's
        /// count. <see cref="AvailableWaitHandle"/> exists to allow a thread to block waiting on multiple
        /// semaphores, but such a wait should be followed by a true wait on the target semaphore.
        /// </remarks>
        /// <exception cref="T:System.ObjectDisposedException">The <see
        /// cref="SemaphoreSlim"/> has been disposed.</exception>
        public WaitHandle AvailableWaitHandle
        {
            get
            {
                CheckDispose();

                // Return it directly if it is not null
                if (m_waitHandle != null)
                    return m_waitHandle;

                //lock the count to avoid multiple threads initializing the handle if it is null
                lock (m_lockObj)
                {
                    if (m_waitHandle == null)
                    {
                        // The initial state for the wait handle is true if the count is greater than zero
                        // false otherwise
                        m_waitHandle = new ManualResetEvent(m_currentCount != 0);
                    }
                }
                return m_waitHandle;
            }
        }

        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="SemaphoreSlim"/> class, specifying
        /// the initial number of requests that can be granted concurrently.
        /// </summary>
        /// <param name="initialCount">The initial number of requests for the semaphore that can be granted
        /// concurrently.</param>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="initialCount"/>
        /// is less than 0.</exception>
        public SemaphoreSlim(int initialCount)
            : this(initialCount, NO_MAXIMUM)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SemaphoreSlim"/> class, specifying
        /// the initial and maximum number of requests that can be granted concurrently.
        /// </summary>
        /// <param name="initialCount">The initial number of requests for the semaphore that can be granted
        /// concurrently.</param>
        /// <param name="maxCount">The maximum number of requests for the semaphore that can be granted
        /// concurrently.</param>
        /// <exception cref="T:System.ArgumentOutOfRangeException"> <paramref name="initialCount"/>
        /// is less than 0. -or-
        /// <paramref name="initialCount"/> is greater than <paramref name="maxCount"/>. -or-
        /// <paramref name="maxCount"/> is less than 0.</exception>
        public SemaphoreSlim(int initialCount, int maxCount)
        {
            if (initialCount < 0 || initialCount > maxCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCount), initialCount, "The initialCount argument must be non-negative and less than or equal to the maximumCount.");
            }

            //validate input
            if (maxCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "The maximumCount argument must be a positive number. If a maximum is not required, use the constructor without a maxCount parameter.");
            }

            m_maxCount = maxCount;
            m_lockObj = new object();
            m_currentCount = initialCount;
        }

        #endregion

        #region  Methods
        /// <summary>
        /// Blocks the current thread until it can enter the <see cref="SemaphoreSlim"/>.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The current instance has already been
        /// disposed.</exception>
        public void Wait()
        {
            // Call wait with infinite timeout
            Wait(Timeout.Infinite, new CancellationToken());
        }

        /// <summary>
        /// Blocks the current thread until it can enter the <see cref="SemaphoreSlim"/>, while observing a
        /// <see cref="T:System.Threading.CancellationToken"/>.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="T:System.Threading.CancellationToken"/> token to
        /// observe.</param>
        /// <exception cref="T:System.OperationCanceledException"><paramref name="cancellationToken"/> was
        /// canceled.</exception>
        /// <exception cref="T:System.ObjectDisposedException">The current instance has already been
        /// disposed.</exception>
        public void Wait(CancellationToken cancellationToken)
        {
            // Call wait with infinite timeout
            Wait(Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// Blocks the current thread until it can enter the <see cref="SemaphoreSlim"/>, using a <see
        /// cref="T:System.TimeSpan"/> to measure the time interval.
        /// </summary>
        /// <param name="timeout">A <see cref="System.TimeSpan"/> that represents the number of milliseconds
        /// to wait, or a <see cref="System.TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <returns>true if the current thread successfully entered the <see cref="SemaphoreSlim"/>;
        /// otherwise, false.</returns>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="timeout"/> is a negative
        /// number other than -1 milliseconds, which represents an infinite time-out -or- timeout is greater
        /// than <see cref="System.Int32.MaxValue"/>.</exception>
        public bool Wait(TimeSpan timeout)
        {
            // Validate the timeout
            Int64 totalMilliseconds = (Int64)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > Int32.MaxValue)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(timeout), timeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
            }

            // Call wait with the timeout milliseconds
            return Wait((int)timeout.TotalMilliseconds, new CancellationToken());
        }

        /// <summary>
        /// Blocks the current thread until it can enter the <see cref="SemaphoreSlim"/>, using a <see
        /// cref="T:System.TimeSpan"/> to measure the time interval, while observing a <see
        /// cref="T:System.Threading.CancellationToken"/>.
        /// </summary>
        /// <param name="timeout">A <see cref="System.TimeSpan"/> that represents the number of milliseconds
        /// to wait, or a <see cref="System.TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">The <see cref="T:System.Threading.CancellationToken"/> to
        /// observe.</param>
        /// <returns>true if the current thread successfully entered the <see cref="SemaphoreSlim"/>;
        /// otherwise, false.</returns>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="timeout"/> is a negative
        /// number other than -1 milliseconds, which represents an infinite time-out -or- timeout is greater
        /// than <see cref="System.Int32.MaxValue"/>.</exception>
        /// <exception cref="System.OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Validate the timeout
            Int64 totalMilliseconds = (Int64)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > Int32.MaxValue)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(timeout), timeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
            }

            // Call wait with the timeout milliseconds
            return Wait((int)timeout.TotalMilliseconds, cancellationToken);
        }

        /// <summary>
        /// Blocks the current thread until it can enter the <see cref="SemaphoreSlim"/>, using a 32-bit
        /// signed integer to measure the time interval.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see
        /// cref="Timeout.Infinite"/>(-1) to wait indefinitely.</param>
        /// <returns>true if the current thread successfully entered the <see cref="SemaphoreSlim"/>;
        /// otherwise, false.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is a
        /// negative number other than -1, which represents an infinite time-out.</exception>
        public bool Wait(int millisecondsTimeout)
        {
            return Wait(millisecondsTimeout, new CancellationToken());
        }


        /// <summary>
        /// Blocks the current thread until it can enter the <see cref="SemaphoreSlim"/>,
        /// using a 32-bit signed integer to measure the time interval,
        /// while observing a <see cref="T:System.Threading.CancellationToken"/>.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see cref="Timeout.Infinite"/>(-1) to
        /// wait indefinitely.</param>
        /// <param name="cancellationToken">The <see cref="T:System.Threading.CancellationToken"/> to observe.</param>
        /// <returns>true if the current thread successfully entered the <see cref="SemaphoreSlim"/>; otherwise, false.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is a negative number other than -1,
        /// which represents an infinite time-out.</exception>
        /// <exception cref="System.OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckDispose();

            // Validate input
            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(millisecondsTimeout), millisecondsTimeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Perf: Check the stack timeout parameter before checking the volatile count
            if (millisecondsTimeout == 0 && m_currentCount == 0)
            {
                // Pessimistic fail fast, check volatile count outside lock (only when timeout is zero!)
                return false;
            }

            uint startTime = 0;
            if (millisecondsTimeout != Timeout.Infinite && millisecondsTimeout > 0)
            {
                startTime = TimeoutHelper.GetTime();
            }

            bool waitSuccessful = false;
            bool lockTaken = false;

            //Register for cancellation outside of the main lock.
            //NOTE: Register/deregister inside the lock can deadlock as different lock acquisition orders could
            //      occur for (1)this.m_lockObj and (2)cts.internalLock
            CancellationTokenRegistration cancellationTokenRegistration = cancellationToken.InternalRegisterWithoutEC(s_cancellationTokenCanceledEventHandler, this);
            try
            {
                // Perf: first spin wait for the count to be positive, but only up to the first planned yield.
                //       This additional amount of spinwaiting in addition
                //       to Monitor.Enter()’s spinwaiting has shown measurable perf gains in test scenarios.
                //
                SpinWait spin = new SpinWait();
                while (m_currentCount == 0 && !spin.NextSpinWillYield)
                {
                    spin.SpinOnce();
                }
                // entering the lock and incrementing waiters must not suffer a thread-abort, else we cannot
                // clean up m_waitCount correctly, which may lead to deadlock due to non-woken waiters.
                try { }
                finally
                {
                    Monitor.Enter(m_lockObj);
                    lockTaken = true;
                    m_waitCount++;
                }

                // If the count > 0 we are good to move on.
                // If not, then wait if we were given allowed some wait duration

                OperationCanceledException oce = null;

                if (m_currentCount == 0)
                {
                    if (millisecondsTimeout == 0)
                    {
                        return false;
                    }

                    // Prepare for the main wait...
                    // wait until the count become greater than zero or the timeout is expired
                    try
                    {
                        waitSuccessful = WaitUntilCountOrTimeout(millisecondsTimeout, startTime, cancellationToken);
                    }
                    catch (OperationCanceledException e) { oce = e; }
                }

                // Now try to acquire.  We prioritize acquisition over cancellation/timeout so that we don't
                // lose any counts when there are asynchronous waiters in the mix.  Asynchronous waiters
                // defer to synchronous waiters in priority, which means that if it's possible an asynchronous
                // waiter didn't get released because a synchronous waiter was present, we need to ensure
                // that synchronous waiter succeeds so that they have a chance to release.
                Debug.Assert(!waitSuccessful || m_currentCount > 0,
                    "If the wait was successful, there should be count available.");
                if (m_currentCount > 0)
                {
                    waitSuccessful = true;
                    m_currentCount--;
                }
                else if (oce != null)
                {
                    throw oce;
                }

                // Exposing wait handle which is lazily initialized if needed
                if (m_waitHandle != null && m_currentCount == 0)
                {
                    m_waitHandle.Reset();
                }
            }
            finally
            {
                // Release the lock
                if (lockTaken)
                {
                    m_waitCount--;
                    Monitor.Exit(m_lockObj);
                }

                // Unregister the cancellation callback.
                cancellationTokenRegistration.Dispose();
            }

            return waitSuccessful;
        }

        /// <summary>
        /// Local helper function, waits on the monitor until the monitor recieves signal or the
        /// timeout is expired
        /// </summary>
        /// <param name="millisecondsTimeout">The maximum timeout</param>
        /// <param name="startTime">The start ticks to calculate the elapsed time</param>
        /// <param name="cancellationToken">The CancellationToken to observe.</param>
        /// <returns>true if the monitor recieved a signal, false if the timeout expired</returns>
        private bool WaitUntilCountOrTimeout(int millisecondsTimeout, uint startTime, CancellationToken cancellationToken)
        {
            int remainingWaitMilliseconds = Timeout.Infinite;

            //Wait on the monitor as long as the count is zero
            while (m_currentCount == 0)
            {
                // If cancelled, we throw. Trying to wait could lead to deadlock.
                cancellationToken.ThrowIfCancellationRequested();

                if (millisecondsTimeout != Timeout.Infinite)
                {
                    remainingWaitMilliseconds = TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout);
                    if (remainingWaitMilliseconds <= 0)
                    {
                        // The thread has expires its timeout
                        return false;
                    }
                }
                // ** the actual wait **
                if (!Monitor.Wait(m_lockObj, remainingWaitMilliseconds))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Exits the <see cref="SemaphoreSlim"/> once.
        /// </summary>
        /// <returns>The previous count of the <see cref="SemaphoreSlim"/>.</returns>
        /// <exception cref="T:System.ObjectDisposedException">The current instance has already been
        /// disposed.</exception>
        public int Release()
        {
            return Release(1);
        }

        /// <summary>
        /// Exits the <see cref="SemaphoreSlim"/> a specified number of times.
        /// </summary>
        /// <param name="releaseCount">The number of times to exit the semaphore.</param>
        /// <returns>The previous count of the <see cref="SemaphoreSlim"/>.</returns>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="releaseCount"/> is less
        /// than 1.</exception>
        /// <exception cref="T:System.Threading.SemaphoreFullException">The <see cref="SemaphoreSlim"/> has
        /// already reached its maximum size.</exception>
        /// <exception cref="T:System.ObjectDisposedException">The current instance has already been
        /// disposed.</exception>
        public int Release(int releaseCount)
        {
            CheckDispose();

            // Validate input
            if (releaseCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(releaseCount), releaseCount, "The releaseCount argument must be greater than zero.");
            }
            int returnCount;

            lock (m_lockObj)
            {
                // Read the m_currentCount into a local variable to avoid unnecessary volatile accesses inside the lock.
                int currentCount = m_currentCount;
                returnCount = currentCount;

                // If the release count would result exceeding the maximum count, throw SemaphoreFullException.
                if (m_maxCount - currentCount < releaseCount)
                {
                    throw new SemaphoreFullException();
                }

                // Increment the count by the actual release count
                currentCount += releaseCount;

                // Signal to any synchronous waiters
                int waitCount = m_waitCount;

                int waitersToNotify = Math.Min(releaseCount, waitCount);
                for (int i = 0; i < waitersToNotify; i++)
                {
                    Monitor.Pulse(m_lockObj);
                }

                m_currentCount = currentCount;

                // Exposing wait handle if it is not null
                if (m_waitHandle != null && returnCount == 0 && currentCount > 0)
                {
                    m_waitHandle.Set();
                }
            }

            // And return the count
            return returnCount;
        }

        /// <summary>
        /// Releases all resources used by the current instance of <see
        /// cref="SemaphoreSlim"/>.
        /// </summary>
        /// <remarks>
        /// Unlike most of the members of <see cref="SemaphoreSlim"/>, <see cref="Dispose()"/> is not
        /// thread-safe and may not be used concurrently with other members of this instance.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// When overridden in a derived class, releases the unmanaged resources used by the
        /// <see cref="T:System.Threading.ManualResetEventSlim"/>, and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources;
        /// false to release only unmanaged resources.</param>
        /// <remarks>
        /// Unlike most of the members of <see cref="SemaphoreSlim"/>, <see cref="Dispose(Boolean)"/> is not
        /// thread-safe and may not be used concurrently with other members of this instance.
        /// </remarks>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_waitHandle != null)
                {
                    m_waitHandle.Close();
                    m_waitHandle = null;
                }
                m_lockObj = null;
            }
        }



        /// <summary>
        /// Private helper method to wake up waiters when a cancellationToken gets canceled.
        /// </summary>
        private static Action<object> s_cancellationTokenCanceledEventHandler = new Action<object>(CancellationTokenCanceledEventHandler);
        private static void CancellationTokenCanceledEventHandler(object obj)
        {
            SemaphoreSlim semaphore = obj as SemaphoreSlim;
            Debug.Assert(semaphore != null, "Expected a SemaphoreSlim");
            lock (semaphore.m_lockObj)
            {
                Monitor.PulseAll(semaphore.m_lockObj); //wake up all waiters.
            }
        }

        /// <summary>
        /// Checks the dispose status by checking the lock object, if it is null means that object
        /// has been disposed and throw ObjectDisposedException
        /// </summary>
        private void CheckDispose()
        {
            if (m_lockObj == null)
            {
                throw new ObjectDisposedException(null, "The semaphore has been disposed.");
            }
        }
        #endregion
    }
}
#endif
