/*
 * Copyright 2019-2026 The Polypheny Project
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Globalization;
using System.Numerics;

namespace PolyphenyDotNetDriver;

/// <summary>An exact Prism decimal value, equal to UnscaledValue * 10^(-Scale).</summary>
public readonly record struct PolyphenyDecimal(BigInteger UnscaledValue, int Scale)
{
    /// <summary>Formats the exact value using a base-10 exponent without rounding or expanding a large scale.</summary>
    public override string ToString()
        => Scale == 0
            ? UnscaledValue.ToString(CultureInfo.InvariantCulture)
            : UnscaledValue.ToString(CultureInfo.InvariantCulture) + "E" + (-(long)Scale).ToString(CultureInfo.InvariantCulture);
}
