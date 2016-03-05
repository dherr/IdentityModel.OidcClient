﻿// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using IdentityModel.Client;
using IdentityModel.OidcClient.WebView;
using System;
using System.Text;
using System.Threading.Tasks;
using PCLCrypto;
using static PCLCrypto.WinRTCrypto;

namespace IdentityModel.OidcClient
{
    public class AuthorizeClient
    {
        private readonly OidcClientOptions _options;

        public AuthorizeClient(OidcClientOptions options)
        {
            _options = options;
        }

        public async Task<AuthorizeState> StartAuthorizeAsync(bool trySilent = false, object extaParameters = null)
        {
            var state = await CreateAuthorizeStateAsync();

            // start webview
            // return state

            var webViewOptions = new InvokeOptions(state.StartUrl, _options.RedirectUri);
            if (trySilent)
            {
                webViewOptions.InitialDisplayMode = DisplayMode.Hidden;
            }
            if (_options.UseFormPost)
            {
                webViewOptions.ResponseMode = ResponseMode.FormPost;
            }

            return null;
        }

        public async Task<AuthorizeResult> AuthorizeAsync(bool trySilent = false, object extraParameters = null)
        {
            InvokeResult wviResult;
            AuthorizeResult result = new AuthorizeResult
            {
                IsError = true,
                State = await CreateAuthorizeStateAsync()
            };

            //var state = await CreateAuthorizeStateAsync();
            

            var webViewOptions = new InvokeOptions(result.State.StartUrl, _options.RedirectUri);
            if (trySilent)
            {
                webViewOptions.InitialDisplayMode = DisplayMode.Hidden;
            }
            if (_options.UseFormPost)
            {
                webViewOptions.ResponseMode = ResponseMode.FormPost;
            }

            wviResult = await _options.WebView.InvokeAsync(webViewOptions);

            if (wviResult.ResultType == InvokeResultType.Success)
            {
                return await ParseResponse(wviResult.Response, result);
            }

            result.Error = wviResult.ResultType.ToString();
            return result;
        }

        public async Task EndSessionAsync(string identityToken = null, bool trySilent = true)
        {
            string url = (await _options.GetProviderInformationAsync()).EndSession;

            if (!string.IsNullOrWhiteSpace(identityToken))
            {
                url += $"?{OidcConstants.EndSessionRequest.IdTokenHint}={identityToken}" +
                       $"&{OidcConstants.EndSessionRequest.PostLogoutRedirectUri}={_options.RedirectUri}";
            }

            var webViewOptions = new InvokeOptions(url, _options.RedirectUri)
            {
                ResponseMode = ResponseMode.Redirect
            };

            if (trySilent)
            {
                webViewOptions.InitialDisplayMode = DisplayMode.Hidden;
            }

            var result = await _options.WebView.InvokeAsync(webViewOptions);
        }

        private async Task<AuthorizeState> CreateAuthorizeStateAsync(object extraParameters = null)
        {
            var state = new AuthorizeState();

            state.Nonce = Guid.NewGuid().ToString("N");
            state.RedirectUri = _options.RedirectUri;

            string codeChallenge = CreateCodeChallenge(state);
            state.StartUrl = await CreateUrlAsync(state, codeChallenge, extraParameters);

            return state;
        }

        private string CreateCodeChallenge(AuthorizeState state)
        {
            if (_options.UseProofKeys)
            {
                state.CodeVerifier = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                var sha256 = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithm.Sha256);

                var challengeBuffer = sha256.HashData(
                    CryptographicBuffer.CreateFromByteArray(
                        Encoding.UTF8.GetBytes(state.CodeVerifier)));
                byte[] challengeBytes;

                CryptographicBuffer.CopyToByteArray(challengeBuffer, out challengeBytes);
                return Base64Url.Encode(challengeBytes);
            }
            else
            {
                return null;
            }
        }

        private async Task<string> CreateUrlAsync(AuthorizeState state, string codeChallenge, object extraParameters)
        {
            var request = new AuthorizeRequest((await _options.GetProviderInformationAsync()).Authorize);
            var url = request.CreateAuthorizeUrl(
                clientId: _options.ClientId,
                responseType: OidcConstants.ResponseTypes.CodeIdToken,
                scope: _options.Scope,
                redirectUri: state.RedirectUri,
                responseMode: _options.UseFormPost ? OidcConstants.ResponseModes.FormPost : null,
                nonce: state.Nonce,
                codeChallenge: codeChallenge,
                codeChallengeMethod: _options.UseProofKeys ? OidcConstants.CodeChallengeMethods.Sha256 : null,
                extra: extraParameters);

            return url;
        }

        private Task<AuthorizeResult> ParseResponse(string webViewResponse, AuthorizeResult result)
        {
            var response = new AuthorizeResponse(webViewResponse);

            if (response.IsError)
            {
                result.Error = response.Error;
                return Task.FromResult(result);
            }

            if (string.IsNullOrEmpty(response.Code))
            {
                result.Error = "Missing authorization code";
                return Task.FromResult(result);
            }

            if (string.IsNullOrEmpty(response.IdentityToken))
            {
                result.Error = "Missing identity token";
                return Task.FromResult(result);
            }

            result.IdentityToken = response.IdentityToken;
            result.Code = response.Code;
            result.IsError = false;

            return Task.FromResult(result);
        }
    }
}