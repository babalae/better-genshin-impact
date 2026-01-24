using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Model.Oauth
{
    public class OauthTokenResponse
    {
        [JsonProperty("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonProperty("token_type")] public string TokenType { get; set; } = string.Empty;
        [JsonProperty("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
        [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
        [JsonProperty("scope")] public string Scope { get; set; } = string.Empty;
        [JsonProperty("jti")] public string Jti { get; set; } = string.Empty;
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }
}
