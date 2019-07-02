using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

[assembly: Parallelizable(ParallelScope.Children)]

// 6 or greater makes it more likely for A(35) and A(55) to start at the same time
[assembly: LevelOfParallelism(6)]

namespace HangRepro
{
    public static class ReproTests
    {
        [Test]
        public static void A([Values(35, 55)] int delay)
        {
            Thread.Sleep(delay);

            // Boom
            new StackTrace(true);
        }

        [Test]
        public static void B([Range(1, 20)] int _)
        {
            Thread.Sleep(20);
        }
    }
}
