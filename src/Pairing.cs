using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

partial class Companion {
    static readonly object pairGate=new object();
    static Dictionary<string,string> paired=new Dictionary<string,string>();
    static string pairId,pairNonce,pairCode,pairToken,pairState;
    static long pairExpires,lastPairStart;
    static string RandomHex(int count){var bytes=new byte[count];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return BitConverter.ToString(bytes).Replace("-","");}
    static string TokenHash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
    static void LoadPairs(){try{paired=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(dataDir,"paired-clients.json")));}catch{paired=new Dictionary<string,string>();}}
    static bool Authorized(string supplied){if(string.IsNullOrEmpty(supplied)||supplied.Length>256)return false;if(supplied==key)return true;lock(pairGate)return paired.ContainsKey(TokenHash(supplied));}
    static object PairStart(string nonce){lock(pairGate){
        if(nonce==null||!Regex.IsMatch(nonce,"^[a-fA-F0-9]{64}$"))return new{state="invalid"};
        if(NowMs()-lastPairStart<10000 || pairState=="pending"&&NowMs()<pairExpires)return new{state="busy"};
        lastPairStart=NowMs();pairId=RandomHex(16);pairNonce=nonce;pairCode=(Convert.ToUInt32(RandomHex(3),16)%1000000).ToString("D6");pairToken=null;pairState="pending";pairExpires=NowMs()+120000;
        return new{state="pending",requestId=pairId,code=pairCode,expiresMs=pairExpires};
    }}
    static object PairStatus(string id,string nonce){lock(pairGate){
        if(id!=pairId||nonce!=pairNonce||NowMs()>pairExpires)return new{state="expired"};
        return new{state=pairState,token=pairState=="approved"?pairToken:null};
    }}
    static string PendingPairCode(){lock(pairGate)return pairState=="pending"&&NowMs()<pairExpires?pairCode:null;}
    static bool DecidePair(string expectedCode,bool approve){lock(pairGate){
        if(pairState!="pending"||NowMs()>pairExpires||expectedCode!=pairCode)return false;
        if(!approve){pairState="denied";return true;}
        if(paired.Count>=8){pairState="limit";return false;}
        string token=RandomHex(32);paired[TokenHash(token)]=DateTime.UtcNow.ToString("o");
        try{AtomicWrite("paired-clients.json",J(paired));}catch{paired.Remove(TokenHash(token));return false;}
        pairToken=token;pairState="approved";return true;
    }}
    static void RevokePairs(){lock(pairGate){paired.Clear();AtomicWrite("paired-clients.json",J(paired));pairState="expired";pairToken=null;key=RandomHex(32);AtomicWrite("access.key",key);}}
    static bool PairingSelfTest(){
        lastPairStart=0;PairStart(new string('A',64));string id=pairId,code=pairCode;
        bool before=pairToken==null&&!Authorized("invalid")&&PendingPairCode()==code;
        bool wrong=!DecidePair("wrong-code",true);
        bool approve=DecidePair(code,true)&&Authorized(pairToken);
        string token=pairToken;bool stored=!File.ReadAllText(Path.Combine(dataDir,"paired-clients.json")).Contains(token);
        bool deniedNonce=!J(PairStatus(id,new string('B',64))).Contains(token);
        pairExpires=0;bool expired=J(PairStatus(id,new string('A',64))).Contains("expired");
        RevokePairs();bool revoked=!Authorized(token);
        lastPairStart=0;PairStart(new string('C',64));bool denied=DecidePair(pairCode,false)&&pairState=="denied"&&pairToken==null;
        return before&&wrong&&approve&&stored&&deniedNonce&&expired&&revoked&&denied;
    }
}
