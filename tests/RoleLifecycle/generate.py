from pathlib import Path
import re, sys
root=Path(__file__).resolve().parents[2]

def method(path,name):
 s=(root/path).read_text(encoding='utf-8-sig')
 m=re.search(r'^    (?:public|private) [^\n]*\b'+name+r'\(',s,re.M)
 if not m: raise Exception(name)
 start=m.start(); line=s[start:s.find('\n',start)]
 if '=>' in line: return line.replace('override ','')
 i=s.find('{',start); depth=1; j=i+1
 while depth:
  depth+=(s[j]=='{')-(s[j]=='}'); j+=1
 return s[start:j].replace('override ','')

def wrapper(name,path,fields,methods):
 return 'class '+name+' {\n'+fields+'\n'+'\n'.join(method(path,n) for n in methods)+'\n}\n'
code='''using System; using System.Collections.Generic; using System.Linq; using static Utils; using UnityEngine; using AmongUs.GameOptions;
'''
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
code+=wrapper('Pelican','Roles/Neutral/Pelican.cs','''public static Dictionary<byte,HashSet<byte>> eatenList=[]; static Dictionary<byte,float> originalSpeed=[]; static Dictionary<byte,Vector2> PelicanLastPosition=[];
public static bool IsEaten(byte id)=>eatenList.Any(x=>x.Value.Contains(id)); private void SyncEatenList() {}''',['IsEaten','CanEat','ReturnEatenPlayerBack'])

output=Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(code,encoding='utf-8')
