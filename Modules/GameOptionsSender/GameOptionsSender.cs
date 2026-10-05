using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;

namespace TOHE.Modules;

public abstract class GameOptionsSender
{
    #region Static
    public readonly static List<GameOptionsSender> AllSenders = new(100) { new NormalGameOptionsSender() };

    public static void SendAllGameOptions()
    {
        AllSenders.RemoveAll(s => !s.AmValid()); // .AmValid() has a virtual property, so it doesn't always return true
        var AllSendersArray = AllSenders.ToArray();
        foreach (GameOptionsSender sender in AllSendersArray)
        {
            if (sender.IsDirty) sender.SendGameOptions();
            sender.IsDirty = false;
        }
    }
    #endregion

    public abstract IGameOptions BasedGameOptions { get; }
    public abstract bool IsDirty { get; protected set; }


    public virtual void SendGameOptions()
    {
        var opt = BuildGameOptions();
        // Keep version framing identical to LogicOptions.Serialize in the game.
        var factory = new GameOptionsFactory(new UnityLogger().Cast<ILogger>());
        SendOptionsArray(factory.ToBytes(opt, AprilFoolsMode.IsAprilFoolsModeToggledOn));
    }
    public virtual void SendOptionsArray(Il2CppStructArray<byte> optionArray)
    {
        try
        {
            byte logicOptionsIndex = 0;
            foreach (var logicComponent in GameManager.Instance.LogicComponents.GetFastEnumerator())
            {
                if (logicComponent.TryCast<LogicOptions>(out _))
                {
                    SendOptionsArray(optionArray, logicOptionsIndex, -1);
                }
                logicOptionsIndex++;
            }
        }
        catch (System.Exception error)
        {
            Logger.Fatal(error.ToString(), "GameOptionsSender.SendOptionsArray");
        }
    }
    protected virtual void SendOptionsArray(Il2CppStructArray<byte> optionArray, byte LogicOptionsIndex, int targetClientId)
    {
        var writer = MessageWriter.Get(SendOption.Reliable);

        writer.StartMessage(targetClientId == -1 ? Tags.GameData : Tags.GameDataTo);
        {
            writer.Write(AmongUsClient.Instance.GameId);
            if (targetClientId != -1) writer.WritePacked(targetClientId);
            writer.StartMessage(1);
            {
                writer.WritePacked(GameManager.Instance.NetId);
                writer.StartMessage(LogicOptionsIndex);
                {
                    writer.WriteBytesAndSize(optionArray);
                }
                writer.EndMessage();
            }
            writer.EndMessage();
        }
        writer.EndMessage();

        AmongUsClient.Instance.SendOrDisconnect(writer);
        writer.Recycle();
    }
    public abstract IGameOptions BuildGameOptions();

    public virtual bool AmValid() => true;
}
