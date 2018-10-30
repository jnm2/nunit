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

namespace NUnit.Framework.Internal
{
    partial class ValueGenerator
    {
        private sealed class ByteValueGenerator : ValueGenerator<byte>
        {
            public override bool TryCreateStep(object value, out ValueGenerator.Step step)
            {
                if (value is byte)
                {
                    step = new ByteStep((byte)value);
                    return true;
                }

                // ByteValueGenerator is unusual in this regard. We allow byte parameter ranges to start high and end low,
                // and internally the step is represented as the Int32 value -1 since it can’t be represented as a Byte.
                // -1 can be converted natively to Int16, SByte and Decimal, so we can fall back on the automatic conversion for them.
                if (value is int)
                {
                    step = new Int32Step((int)value);
                    return true;
                }

                return base.TryCreateStep(value, out step);
            }

            private sealed class ByteStep : Step
            {
                private readonly byte _stepValue;

                public ByteStep(byte value)
                {
                    _stepValue = value;
                }

                public override bool TryApply(byte value, out byte nextValue)
                {
                    nextValue = (byte)(value + _stepValue);
                    return nextValue > value;
                }

                public override bool IsPositive => _stepValue > 0;
                public override bool IsNegative => false;
            }

            private sealed class Int32Step : Step
            {
                private readonly int _stepValue;

                public Int32Step(int value)
                {
                    _stepValue = value;
                }

                public override bool TryApply(byte value, out byte nextValue)
                {
                    nextValue = (byte)(value + _stepValue);
                    return _stepValue > 0 ? nextValue > value : nextValue < value;
                }

                public override bool IsPositive => _stepValue > 0;
                public override bool IsNegative => _stepValue < 0;
            }
        }
    }
}
