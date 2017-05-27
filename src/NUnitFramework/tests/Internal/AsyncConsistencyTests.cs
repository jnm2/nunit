#if ASYNC
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal.Builders;
using NUnit.Framework.Internal.Commands;
using NUnit.TestUtilities;

namespace NUnit.Framework.Internal
{
    [TestFixture]
    public sealed class AsyncConsistencyTests
    {
        public static readonly IEnumerable<TestHarness> TestHarnesses = new TestHarness[]
        {
            new TestMethodTestHarness(),
            new SetupMethodTestHarness(),
            new TeardownMethodTestHarness(),
            new ThrowsConstraintTestHarness(),
            new AssertThrowsTestHarness()
        };


        [TestCaseSource(nameof(TestHarnesses))]
        public void Async_void_is_not_allowed(TestHarness testHarness)
        {
            testHarness.AssertAsyncVoidIsNotAllowed(new TestDelegate(Fixture.Async_void));
        }


        [TestCaseSource(nameof(TestHarnesses))]
#if NET_4_0
        [Ignore("Not supported by async polyfill for .NET 4.0.")]
#endif
        public void Async_thrown_OperationCanceledException_should_not_change_type(TestHarness testHarness)
        {
            testHarness.AssertExactExceptionType(new AsyncTestDelegate(Fixture.Throws_OperationCanceledException_async), typeof(OperationCanceledException));
        }

        private static class Fixture
        {
            public static async Task Throws_OperationCanceledException_async()
            {
#if NET_4_0
                await TaskEx.Yield();
#else
                await Task.Yield();
#endif
                throw new OperationCanceledException();
            }
            public static async void Async_void()
            {
#if NET_4_0
                await TaskEx.FromResult<object>(null);
#else
                await Task.FromResult<object>(null);
#endif
            }
        }





        public abstract class TestHarness
        {
            public abstract void AssertExactExceptionType(Delegate @delegate, Type exceptionType);

            public abstract void AssertAsyncVoidIsNotAllowed(Delegate @delegate);

            public override string ToString() => GetType().Name;
        }

        private sealed class AssertThrowsTestHarness : TestHarness
        {
            public override void AssertExactExceptionType(Delegate @delegate, Type exceptionType)
            {
                var testDelegate = @delegate as TestDelegate;
                if (testDelegate != null)
                {
                    Assert.Throws(exceptionType, testDelegate);
                    return;
                }

                var asyncTestDelegate = @delegate as AsyncTestDelegate;
                if (asyncTestDelegate != null)
                {
                    Assert.ThrowsAsync(exceptionType, asyncTestDelegate);
                    return;
                }

                if (@delegate == null) throw new ArgumentNullException(nameof(@delegate));
                throw new NotSupportedException(@delegate.GetType().FullName);
            }

            public override void AssertAsyncVoidIsNotAllowed(Delegate @delegate)
            {
                var testDelegate = @delegate as TestDelegate;
                if (testDelegate != null)
                {
                    Assert.That(() =>
                    {
                        Assert.Throws(Is.Null, testDelegate);
                    }, Throws.TypeOf<ArgumentException>());
                    return;
                }

                if (@delegate == null) throw new ArgumentNullException(nameof(@delegate));
                throw new NotSupportedException(@delegate.GetType().FullName);
            }
        }

        private sealed class ThrowsConstraintTestHarness : TestHarness
        {
            public override void AssertExactExceptionType(Delegate @delegate, Type exceptionType)
            {
                Assert.That(@delegate, Throws.TypeOf(exceptionType));
            }

            public override void AssertAsyncVoidIsNotAllowed(Delegate @delegate)
            {
                Assert.That(() =>
                {
                    Assert.That(@delegate, Throws.Nothing);
                }, Throws.TypeOf<ArgumentException>());
            }
        }

        private sealed class TestMethodTestHarness : TestHarness
        {
            public override void AssertExactExceptionType(Delegate @delegate, Type exceptionType)
            {
                var result = RunDelegate(@delegate);
                Assert.That(result.ResultState.Matches(ResultState.Failure));
                Assert.That(result.Message, Does.StartWith($"{exceptionType} : "));
            }

            public override void AssertAsyncVoidIsNotAllowed(Delegate @delegate)
            {
                var result = RunDelegate(@delegate, allowNonRunnable: true);
                Assert.That(result.ResultState.Matches(ResultState.NotRunnable));
                Assert.That(result.Message.IndexOf("async", StringComparison.InvariantCultureIgnoreCase) != -1);
                Assert.That(result.Message, Contains.Substring("method must have non-void return type"));
            }


            private static ITestResult RunDelegate(Delegate @delegate, bool allowNonRunnable = false)
            {
#if PORTABLE || NETSTANDARD1_6
                var method = @delegate.GetMethodInfo();
#else
                var method = @delegate.Method;
#endif
                var test = new DefaultTestCaseBuilder().BuildFrom(new MethodWrapper(method.DeclaringType, method));
                var result = TestBuilder.RunTest(test, @delegate.Target);

                if (!allowNonRunnable && result.ResultState.Matches(ResultState.NotRunnable))
                    Assert.Fail($"Method {method.Name} is not runnable as a test method.");

                return result;
            }
        }

        private abstract class SetupTeardownMethodTestHarness : TestHarness
        {
            protected abstract void Run(MethodInfo method, TestExecutionContext context);

            protected ITestResult RunDelegate(Delegate @delegate)
            {
#if PORTABLE || NETSTANDARD1_6
                var method = @delegate.GetMethodInfo();
#else
                var method = @delegate.Method;
#endif

                var context = new TestExecutionContext
                {
                    TestObject = @delegate.Target,
                    CurrentResult = new TestCaseResult(new TestMethod(new MethodWrapper(method.DeclaringType, method)))
                };

                Run(method, context);

                return context.CurrentResult;
            }
        }

        private sealed class SetupMethodTestHarness : SetupTeardownMethodTestHarness
        {
            public override void AssertExactExceptionType(Delegate @delegate, Type exceptionType)
            {
                Assert.That(() => RunDelegate(@delegate), Throws.TypeOf(exceptionType));
            }

            public override void AssertAsyncVoidIsNotAllowed(Delegate @delegate)
            {
                var result = RunDelegate(@delegate);
                Assert.That(result.ResultState.Matches(ResultState.NotRunnable));
                Assert.That(result.Message.IndexOf("async", StringComparison.InvariantCultureIgnoreCase) != -1);
                Assert.That(result.Message, Contains.Substring("method must have non-void return type"));
            }

            protected override void Run(MethodInfo method, TestExecutionContext context)
            {
                var item = new SetUpTearDownItem(new[] { method }, new MethodInfo[0]);
                item.RunSetUp(context);
            }
        }

        private sealed class TeardownMethodTestHarness : SetupTeardownMethodTestHarness
        {
            public override void AssertExactExceptionType(Delegate @delegate, Type exceptionType)
            {
                var result = RunDelegate(@delegate);
                Assert.That(result.Message, Does.StartWith($"TearDown : {exceptionType} : "));
            }

            public override void AssertAsyncVoidIsNotAllowed(Delegate @delegate)
            {
                var result = RunDelegate(@delegate);
                Assert.That(result.ResultState.Matches(ResultState.NotRunnable));
                Assert.That(result.Message.IndexOf("async", StringComparison.InvariantCultureIgnoreCase) != -1);
                Assert.That(result.Message, Contains.Substring("method must have non-void return type"));
            }

            protected override void Run(MethodInfo method, TestExecutionContext context)
            {
                var item = new SetUpTearDownItem(new MethodInfo[0], new[] { method });
                item.RunSetUp(context);
                item.RunTearDown(context);
            }
        }
    }
}
#endif
