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

using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;

namespace PolyphenyDotNetDriver;

internal static class PrismStatement
{
    internal static async Task<Response> ExecuteUnparameterizedAsync(
        Request request,
        Func<Request, Task<Response>> sendRecv,
        Func<int, Task<Response>> receive,
        string mismatchMessage,
        IPolyphenyConnection connection = null)
    {
        var sessionVersion = (connection as PolyphenyConnection)?.SessionVersion;
        var acknowledgment = await sendRecv(request).ConfigureAwait(false);
        PrismResponse.ThrowIfError(acknowledgment, request.Id);
        PrismResponse.ValidateRequestId(acknowledgment, request.Id);
        if (acknowledgment?.Last == true)
        {
            throw new InvalidDataException("Expected an intermediate Prism statement acknowledgment.");
        }
        var statementId = acknowledgment?.StatementResponse?.StatementId;

        Response response;
        try
        {
            response = await receive(8).ConfigureAwait(false);
            PrismResponse.ThrowIfError(response, request.Id);
        }
        catch (PolyphenyException) when (statementId.HasValue && IsCurrentSession(connection, sessionVersion))
        {
            await TryCloseAsync(sendRecv, statementId.Value).ConfigureAwait(false);
            throw;
        }

        PrismResponse.ValidateRequestId(response, request.Id);
        if (response?.StatementResponse?.StatementId != statementId)
        {
            throw new Exception(mismatchMessage);
        }

        return response;
    }

    internal static async Task CloseAsync(Func<Request, Task<Response>> sendRecv, int statementId)
    {
        var request = new Request
        {
            CloseStatementRequest = new CloseStatementRequest { StatementId = statementId }
        };
        var response = await sendRecv(request).ConfigureAwait(false);
        PrismResponse.ValidateStatementClose(response, request.Id);
    }

    internal static async Task TryCloseAsync(Func<Request, Task<Response>> sendRecv, int statementId)
    {
        try
        {
            await CloseAsync(sendRecv, statementId).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original error if cleanup also fails.
        }
    }

    internal static bool IsCurrentSession(IPolyphenyConnection connection, long? version)
        => connection is not PolyphenyConnection physical
            || physical.State == ConnectionState.Open && physical.SessionVersion == version;
}
