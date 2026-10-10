using System.Security.Cryptography;
using System.Text;

namespace SmartCore.Identity;

// Only an admission boundary that has NOT invoked Refresh may issue this receipt.
public sealed record RefreshRejection(string Code,string Disposition,Guid RequestReference,int RetryAfterSeconds,string Proof)
{
    public static bool Allowed(int status,string code)=>status==429 && code=="RATE_LIMITED" || status==503 && code=="AUTHENTICATION_BUSY";
    private static byte[] Mac(string key,int status,string code,Guid reference,int retry)=>HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(key),Encoding.UTF8.GetBytes($"identity-refresh-not-processed-v1\n{status}\n{code}\n{reference:D}\n{retry}"));
    public static RefreshRejection Create(string key,int status,string code,Guid reference,int retry)
    {
        if(!Allowed(status,code) || reference==Guid.Empty || retry is <1 or >60)throw new ArgumentException("Invalid admission rejection");
        return new(code,"NotProcessed",reference,retry,Convert.ToBase64String(Mac(key,status,code,reference,retry)));
    }
    public bool Verify(string key,int status,Guid reference)
    {
        if(!Allowed(status,Code) || Disposition!="NotProcessed" || reference==Guid.Empty || RequestReference!=reference || RetryAfterSeconds is <1 or >60)return false;
        try{return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Proof),Mac(key,status,Code,reference,RetryAfterSeconds));}
        catch(FormatException){return false;}
    }
}
