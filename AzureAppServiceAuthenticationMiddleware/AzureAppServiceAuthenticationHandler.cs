﻿using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Middleware.Authentication.AppService
{
    public class AzureAppServiceAuthenticationHandler : AuthenticationHandler<AzureAppServiceAuthenticationOptions>
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Logger.LogInformation("starting authentication handler for app service authentication");

            if (this.Context.User == null || this.Context.User.Identity == null || this.Context.User.Identity.IsAuthenticated == false)
            {
                Logger.LogInformation("identity not found, attempting to fetch from auth endpoint /.auth/me");

                var cookieContainer = new CookieContainer();

                HttpClientHandler handler = new HttpClientHandler()
                {
                    CookieContainer = cookieContainer
                };

                var uriString = $"{Context.Request.Scheme}://{Context.Request.Host}";

                Logger.LogDebug("host uri: {0}", uriString);

                foreach (var c in Context.Request.Cookies)
                {
                    cookieContainer.Add(new Uri(uriString), new Cookie(c.Key, c.Value));
                }

                Logger.LogDebug("found {0} cookies in request", cookieContainer.Count);

                foreach(var cookie in Context.Request.Cookies)
                {
                    Logger.LogDebug(cookie.Key);
                }

                //fetch value from endpoint
                var request = new HttpRequestMessage(HttpMethod.Get, $"{uriString}/.auth/me");

                foreach ( var header in Context.Request.Headers){
                    if(header.Key.StartsWith("X-ZUMO-")){
                        request.Headers.Add(header.Key,header.Value[0]);
                    }
                }

                JArray payload = null;

                using (HttpClient client = new HttpClient(handler))
                {
                    try
                    {
                        var response = await client.SendAsync(request);
                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.LogDebug("auth endpoint was not sucessful. Status code: {0}, reason {1}", response.StatusCode, response.ReasonPhrase);
                            return AuthenticateResult.Fail("Unable to fetch user information from auth endpoint.");
                        }

                        var content = await response.Content.ReadAsStringAsync();

                        payload = JArray.Parse(content);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex.Message);
                    }
                };

                if(payload == null)
                {
                    return AuthenticateResult.Fail("Could not retreive json from /me endpoint.");
                }

                //build up identity from json...
                var id = payload[0]["user_id"].Value<string>();
                var idToken = payload[0]["id_token"].Value<string>();
                var providerName = payload[0]["provider_name"].Value<string>();

                Logger.LogDebug("payload was fetched from endpoint. id: {0}", id);

                var identity = new GenericIdentity(id);

                Logger.LogInformation("building claims from payload...");

                List<Claim> claims = new List<Claim>();
                foreach (var claim in payload[0]["user_claims"])
                {
                    claims.Add(new Claim(claim["typ"].ToString(), claim["val"].ToString()));
                }

                Logger.LogInformation("Add claims to new identity");

                identity.AddClaims(claims);
                identity.AddClaim(new Claim("id_token", idToken));
                identity.AddClaim(new Claim("provider_name", providerName));

                ClaimsPrincipal p = new GenericPrincipal(identity, null); //todo add roles?

                var ticket = new AuthenticationTicket(p,
                    new Microsoft.AspNetCore.Http.Authentication.AuthenticationProperties(),
                    Options.AuthenticationScheme);

                Logger.LogInformation("Set identity to user context object.");
                this.Context.User = p;

                Logger.LogInformation("identity build was a success, returning ticket");
                return AuthenticateResult.Success(ticket);

            }

            Logger.LogInformation("identity already set, skipping middleware");
            return AuthenticateResult.Skip();           
        }
    }
}
