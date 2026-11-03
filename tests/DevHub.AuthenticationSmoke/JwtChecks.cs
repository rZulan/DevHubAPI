using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevHub.Application.Abstractions.Authentication;
using DevHub.Domain.Users;
using Microsoft.Extensions.Configuration;

internal static class JwtChecks
{
    public static async Task Run(IServiceProvider services,HttpClient client,Action<string,bool> check)
    {
        var user=User.Create("jwt-smoke@example.invalid","jwt_smoke","JWT","Smoke","unused",DateTimeOffset.UtcNow);
        var generated=services.GetRequiredService<IJwtTokenGenerator>().Generate(user).AccessToken;
        var parts=generated.Split('.');
        var header=JsonSerializer.Deserialize<Dictionary<string,object?>>(Base64Url(parts[0]))!;
        var payload=JsonSerializer.Deserialize<Dictionary<string,object?>>(Base64Url(parts[1]))!;
        var key=Encoding.UTF8.GetBytes(services.GetRequiredService<IConfiguration>()["Jwt:Key"]!);
        string Token(Dictionary<string,object?> h,Dictionary<string,object?>? p=null,byte[]? signingKey=null)
        {
            var data=Encode(h)+"."+Encode(p??payload);
            var alg=h["alg"]?.ToString();
            return data+"."+(alg=="none"?"":Convert.ToBase64String(HMACSHA256.HashData(signingKey??key,Encoding.ASCII.GetBytes(data))).TrimEnd('=').Replace('+','-').Replace('/','_'));
        }
        async Task Probe(string name,string? token,HttpStatusCode expected=HttpStatusCode.Unauthorized)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"/authentication-smoke/jwt");
            if(token is not null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            using var response=await client.SendAsync(request);
            check(name,response.StatusCode==expected);
        }
        await Probe("JWT anonymous request rejected",null);
        await Probe("current HS256 JWT remains valid",generated,HttpStatusCode.OK);
        await Probe("JWT payload tampering rejected",parts[0]+"."+Encode(new Dictionary<string,object?>(payload){["sub"]=Guid.NewGuid().ToString(),["role"]="admin"})+"."+parts[2]);
        await Probe("JWT untrusted signing key rejected",Token(header,signingKey:RandomNumberGenerator.GetBytes(64)));
        foreach(var alg in new[]{"HS384","HS512"})
        {
            var h=new Dictionary<string,object?>(header){["alg"]=alg};var data=Encode(h)+"."+Encode(payload);
            var signature=alg=="HS384"?HMACSHA384.HashData(key,Encoding.ASCII.GetBytes(data)):HMACSHA512.HashData(key,Encoding.ASCII.GetBytes(data));
            await Probe($"JWT correctly signed {alg} rejected",data+"."+Convert.ToBase64String(signature).TrimEnd('=').Replace('+','-').Replace('/','_'));
        }
        await Probe("JWT unsigned algorithm rejected",Token(new(header){["alg"]="none"}));
        await Probe("JWT alternative typ rejected",Token(new(header){["typ"]="id+jwt"}));
        var noType=new Dictionary<string,object?>(header);noType.Remove("typ");
        await Probe("JWT missing typ rejected",Token(noType));
        foreach(var critical in new object?[]{new[]{"audit-extension"},Array.Empty<string>(),null,"audit-extension",true,42})
            await Probe($"JWT critical header rejected ({JsonSerializer.Serialize(critical)})",Token(new(header){["crit"]=critical,["audit-extension"]=true}));
        foreach(var claim in new[]{"iss","aud","exp"})
        {
            var p=new Dictionary<string,object?>(payload);p.Remove(claim);
            await Probe($"JWT missing {claim} rejected",Token(header,p));
        }
        await Probe("JWT wrong issuer rejected",Token(header,new(payload){["iss"]="untrusted"}));
        await Probe("JWT wrong audience rejected",Token(header,new(payload){["aud"]="untrusted"}));
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await Probe("JWT expiry beyond skew rejected",Token(header,new(payload){["nbf"]=now-600,["exp"]=now-180}));
        await Probe("JWT future nbf rejected",Token(header,new(payload){["nbf"]=now+300,["exp"]=now+600}));
        await Probe("JWT harmless noncritical metadata accepted",Token(new(header){["audit-metadata"]=true}),HttpStatusCode.OK);
    }

    private static string Encode(object value)=>Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static byte[] Base64Url(string value)=>Convert.FromBase64String(value.Replace('-','+').Replace('_','/').PadRight((value.Length+3)/4*4,'='));
}
