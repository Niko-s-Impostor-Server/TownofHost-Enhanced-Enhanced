using AmongUs.GameOptions;
using System;

namespace TOHE.Modules;

public class NormalGameOptionsSender : GameOptionsSender
{
    private LogicOptions _logicOptions;
    public override IGameOptions BasedGameOptions
        => GameOptionsManager.Instance.CurrentGameOptions;

    public override bool IsDirty
    {
        get
        {
            var manager = GameManager.Instance;
            if (manager == null) return false;
            try
            {
                if (_logicOptions == null || !manager.LogicComponents.Contains(_logicOptions))
                {
                    _logicOptions = null;
                    foreach (var glc in manager.LogicComponents.GetFastEnumerator())
                        if (glc.TryCast<LogicOptions>(out var lo))
                            _logicOptions = lo;
                }
                return _logicOptions != null && _logicOptions.IsDirty;
            }
            catch (Exception error)
            {
                Logger.Warn(error.ToString(), "NormalGameOptionsSender.IsDirty.Get");
                return false;
            }
        }
        protected set
        {
            try
            {
                _logicOptions?.ClearDirtyFlag();
            }
            catch (Exception error)
            {
                Logger.Warn(error.ToString(), "NormalGameOptionsSender.IsDirty.ProtectedSet");
            }
        }
    }

    public override IGameOptions BuildGameOptions()
        => BasedGameOptions;
}
