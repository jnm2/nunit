// ***********************************************************************
// Copyright (c) 2018 Charlie Poole, Rob Prouse
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;

namespace NUnit.Framework.Internal
{
    partial class ValueGenerator
    {
        private sealed class SingleValueGenerator : ValueGenerator<float>
        {
            public override bool TryCreateStep(object value, out ValueGenerator.Step step)
            {
                if (value is float)
                {
                    step = new SingleStep((float)value);
                    return true;
                }

                return base.TryCreateStep(value, out step);
            }

            private sealed class SingleStep : Step
            {
                private readonly float _stepValue;

                public SingleStep(float value)
                {
                    _stepValue = value;
                }

                public override bool TryApply(float value, out float nextValue)
                {
                    nextValue = value + _stepValue;
                    if (_stepValue > 0 ? nextValue <= value : value <= nextValue)
                        throw new ArithmeticException($"Not enough precision to represent the next step; {value:r} + {_stepValue:r} = {nextValue:r}.");
                    return true;
                }

                public override bool IsPositive => _stepValue > 0;
                public override bool IsNegative => _stepValue < 0;
            }
        }
    }
}
