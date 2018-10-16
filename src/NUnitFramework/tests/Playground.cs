#if NET45
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework.Internal;

namespace NUnit.Framework
{
    // We can kill several related birds with one stone if this sits below work items but above test commands.
    public interface IManageTestExecution
    {
        ITestCaseExecutor CreateExecutor();
    }

    /// <summary>
    /// A test case executor is created per parallel worker and disposed once there
    /// are no longer any tests to run to which it applies.
    /// </summary>
    public interface ITestCaseExecutor
    {
        /// <summary>
        /// Will never be called for the same executor before the previous call returns.
        /// </summary>
        void ExecuteTestCase(ExecuteTestCaseArgs args);
    }

    /// <summary>
    /// An optional base class which sets appropriate attribute usage and implements <see cref="IManageTestExecution"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public abstract class TestCaseExecutionManagerAttribute : NUnitAttribute, IManageTestExecution
    {
        protected abstract ITestCaseExecutor CreateExecutor();

        ITestCaseExecutor IManageTestExecution.CreateExecutor() => CreateExecutor();
    }

    // Demo for https://github.com/nunit/nunit/issues/3013#issuecomment-420447250
    public sealed class RunTestsOnExistingThreadAttribute : Attribute, IManageTestExecution
    {
        public ITestCaseExecutor CreateExecutor() => new Executor();

        private sealed class Executor : ITestCaseExecutor
        {
            public void ExecuteTestCase(ExecuteTestCaseArgs args)
            {
                Application.Instance.Invoke(() => args.StartAndWait());
            }
        }
    }

    public sealed class WindowsFormsMessageLoopAttribute : TestCaseExecutionManagerAttribute
    {
        protected override ITestCaseExecutor CreateExecutor() => new Executor();

        private sealed class Executor : ITestCaseExecutor, IMessageLoop
        {
            public void ExecuteTestCase(ExecuteTestCaseArgs args)
            {
                args.RunWithMessageLoop(this);
            }

            public void RunLoop()
            {
                Application.Run();
            }

            public void ExitLoop()
            {
                Application.Exit();
            }

            public void PostToLoop(Action work)
            {
                var context = SynchronizationContext.Current as WindowsFormsSynchronizationContext;
                if (context is null)
                {
                    context = new WindowsFormsSynchronizationContext();
                    SynchronizationContext.SetSynchronizationContext(context);
                }

                // Better-performing equivalent of context.Post(_ => work.Invoke(), null);
                context.Post(state => ((Action)state).Invoke(), state: work);
            }
        }
    }

    /// <summary>
    /// Encapsulates
    /// </summary>
    public sealed class ExecuteTestCaseArgs
    {
        private readonly Func<AwaitAdapter> _work;

        internal ExecuteTestCaseArgs(Func<AwaitAdapter> work)
        {
            _work = work;
        }

        public void StartAndWait()
        {
            var originalState = SandboxedThreadState.Capture();
            try
            {
                _work.Invoke().BlockUntilCompleted();
            }
            finally
            {
                originalState.Restore();
            }
        }

        // Good because we can add new methods in future releases
        public void RunWithMessageLoop(IMessageLoop messageLoop)
        {
            messageLoop.PostToLoop(() =>
            {
                var originalState = SandboxedThreadState.Capture();
                var awaitable = _work.Invoke();

                if (awaitable.IsCompleted)
                {
                    originalState.Restore();
                    messageLoop.ExitLoop();
                }
                else
                {
                    awaitable.OnCompleted(() =>
                    {
                        messageLoop.PostToLoop(() =>
                        {
                            originalState.Restore();
                            messageLoop.ExitLoop();
                        });
                    });
                }
            });

            messageLoop.RunLoop();
        }
    }

    public interface IMessageLoop
    {
        void RunLoop();
        void ExitLoop();
        void PostToLoop(Action work);
    }

    public static class Playground
    {
        [Test, WindowsFormsMessageLoop]
        public static async Task SomeTest()
        {
        }
    }
}
#endif
