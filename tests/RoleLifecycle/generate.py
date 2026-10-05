from pathlib import Path
import re, sys
root=Path(__file__).resolve().parents[2]

def method(path,name,scope=None):
 s=(root/path).read_text(encoding='utf-8-sig')
 if scope: s=s[s.index(scope):]
 m=re.search(r'^    (?:public|private|internal) [^\n]*\b'+name+r'\(',s,re.M)
 if not m: raise Exception(name)
 start=m.start(); line=s[start:s.find('\n',start)]
 if '=>' in line: return line.replace('override ','')
 i=s.find('{',start); arrow=s.find('=>',start)
 if arrow>=0 and arrow<i: return s[start:s.find(';',arrow)+1].replace('override ','')
 depth=1; j=i+1
 while depth:
  depth+=(s[j]=='{')-(s[j]=='}'); j+=1
 return s[start:j].replace('override ','')

def wrapper(name,path,fields,methods,scope=None):
 return 'class '+name+' {\n'+fields+'\n'+'\n'.join(method(path,n,scope) for n in methods)+'\n}\n'
code='''using System; using System.Collections.Generic; using System.Linq; using static Utils; using UnityEngine; using AmongUs.GameOptions; using TOHE;
'''
rpc_source=(root/'Modules/RPC.cs').read_text(encoding='utf-8-sig')
code+=re.search(r'enum CustomRPC : byte[^\n]*\n\{.*?\n\}',rpc_source,re.S)[0]+'\n'
code+=wrapper('Agitater','Roles/Neutral/Agitater.cs','''public static HashSet<byte> playerIdList=[1]; public static bool HasEnabled=>playerIdList.Any();
public static byte CurrentBombedPlayer,LastBombedPlayer; public static bool AgitaterHasBombed; public static long? CurrentBombedPlayerTime,AgitaterBombedTime; static uint BombGeneration;
static OptionItem AgitaterAutoReportBait=new(0),BombExplodeCooldown=new(10);''',['Init','ResetBomb','OnCheckMurderAsKiller'])
code+=wrapper('Deathpact','Roles/Impostor/Deathpact.cs','''public static HashSet<byte> Playerids=[],ActiveDeathpacts=[]; public static Dictionary<byte,HashSet<PlayerControl>> PlayersInDeathpact=[]; public static Dictionary<byte,long> DeathpactTime=[];
static OptionItem NumberOfPlayersInPact=new(2),DeathpactDuration=new(20),ShowArrowsToOtherPlayersInPact=new(1),ReduceVisionWhileInPact=new(1);
public static OptionItem KillDeathpactPlayersOnMeeting=new(1); public static OptionItem VisionWhileInPact=new(.65f);''',['Add','Remove','DoDeathpact','ClearDeathpact','OnReportDeadBody','KillPlayerInDeathpact','CheckCancelDeathpact','SetDeathpactVision'])
code+=wrapper('Mastermind','Roles/Impostor/Mastermind.cs','''public static HashSet<byte> playerIdList=[1]; public static Dictionary<byte,long> ManipulatedPlayers=[]; public static Dictionary<byte,float> TempKCDs=[]; static OptionItem KillCooldown=new(25);''',['PlayerIsManipulated','CheckMurderOnOthersTarget'])
code+=wrapper('Vampire','Roles/Impostor/Vampire.cs','''public class BittenInfo(byte vampireId,float killTimer) { public byte VampireId=vampireId; public float KillTimer=killTimer; }
public static Dictionary<byte,BittenInfo> BittenPlayers=[]; static float KillDelay=10;''',['OnFixedUpdate','KillBitten','OnReportDeadBody'])
code+=wrapper('Poisoner','Roles/Neutral/Poisoner.cs','''public class PoisonedInfo(byte poisonerId,float killTimer) { public byte PoisonerId=poisonerId; public float KillTimer=killTimer; }
public static Dictionary<byte,PoisonedInfo> PoisonedPlayers=[]; static float KillDelay=10;''',['OnFixedUpdate','KillPoisoned','OnReportDeadBody'])
code+=wrapper('Fireworker','Roles/Impostor/Fireworker.cs','''public enum FireworkerState { Initial=1, SettingFireworker=2, WaitTime=4, ReadyFire=8, FireEnd=16 }
public static Dictionary<byte,FireworkerState> state=[]; public static Dictionary<byte,int> nowFireworkerCount=[];''',['GetLowerText','SendRPC'])
code+=wrapper('Instigator','Roles/Impostor/Instigator.cs','''public PlayerControl _Player; public float AbilityLimit=1; static OptionItem KillsPerAbilityUse=new(1); public void SendSkillRPC() {}''',['OnPlayerExiled'])
code+=wrapper('Pelican','Roles/Neutral/Pelican.cs','''public static Dictionary<byte,HashSet<byte>> eatenList=[]; public static Dictionary<byte,float> originalSpeed=[]; public static Dictionary<byte,Vector2> PelicanLastPosition=[]; public static Dictionary<byte,Vector2> lastKnownPosition=[]; static int Count; public static int Syncs;
public static bool IsEaten(byte id)=>eatenList.Any(x=>x.Value.Contains(id)); private void SyncEatenList() { Syncs++; } private static Vector2 GetBlackRoomPSForPelican()=>new();''',['Init','IsEaten','CanEat','Remove','ReturnEatenPlayerBack','ReleaseEatenPlayers','OnMurderPlayerAsTarget','OnFixedUpdate','SendOwnerClear','ReceiveOwnerClear'])
code+=wrapper('Shroud','Roles/Neutral/Shroud.cs','''public static Dictionary<byte,byte> ShroudList=[]; public PlayerState _state; public PlayerControl _Player=>_state==null?null:Utils.GetPlayerById(_state.PlayerId);
''',['Init','Add','Remove','ClearShrouds','SendRPC','OnFixedUpdateOthers','OnPlayerExiled','AfterMeetingTasks','SendOwnerClear','ReceiveOwnerClear'])
dispatch=method('Modules/RPC.cs','DispatchCustomRpc','internal class RPCHandlerPatch')
entry=dispatch[:dispatch.index('        switch (rpcType)')]
owner_dispatch='\n'.join(re.search(r'            case CustomRPC.'+name+r':\n.*?                break;',dispatch,re.S)[0] for name in ['ClearPelicanOwner','ClearShroudOwner'])
code+=wrapper('RPCHandlerPatch','Modules/RPC.cs','public static int Dispatches;',['TrustedRpc','Prefix','ValidateRpc'],'internal class RPCHandlerPatch')[:-2]+entry+'        switch (rpcType) {\n'+owner_dispatch+'\n        }\n        Dispatches++;\n    }\n}\n'
code+=wrapper('CustomRpcReceiver','Modules/CustomRpcReceiver.cs','',['Receive'])
code+='static partial class CustomRpcTransport {\n'+method('Modules/Rpc/CustomRpcTransport.cs','IsValidRpcId')+'\n}\n'

output=Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(code,encoding='utf-8')
