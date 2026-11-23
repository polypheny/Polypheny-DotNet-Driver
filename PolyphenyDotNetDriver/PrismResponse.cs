/*
 * Copyright 2019-2024 The Polypheny Project
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

using System.IO;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver;

internal static class PrismResponse
{
    internal static void ValidateRequestId(Response response, ulong? requestId)
    {
        if (response != null && requestId.HasValue && response.Id != requestId.Value)
        {
            throw new InvalidDataException("The Prism response ID does not match its request.");
        }
    }

    internal static void ThrowIfError(Response response, ulong? requestId = null)
    {
        if (response?.ErrorResponse is not { } error)
        {
            return;
        }

        ValidateRequestId(response, requestId);
        if (!response.Last)
        {
            throw new InvalidDataException("A Prism error response must be final.");
        }

        var details = error.Details;
        throw new PolyphenyException(
            error.Message,
            details?.HasState == true ? details.State : null,
            details?.HasErrorCode == true ? details.ErrorCode : null);
    }

    internal static void ValidateStatementClose(Response response, ulong? requestId = null)
    {
        ThrowIfError(response, requestId);
        ValidateRequestId(response, requestId);
        if (response?.CloseStatementResponse == null || !response.Last)
        {
            throw new InvalidDataException("Expected a final Prism statement-close response.");
        }
    }
}
