using System;

namespace TOHE;

class LateTask
{
    public string name;
    public float timer;
    public bool shouldLog;
    public Action action;
    private bool completed;
    public static List<LateTask> Tasks = [];
    public bool Run(float deltaTime)
    {
        if (completed) return true;
        timer -= deltaTime;
        if (timer <= 0)
        {
            // Remove before invoking callbacks so a nested update cannot run this task twice.
            completed = true;
            Tasks.Remove(this);
            action();
            return true;
        }
        return false;
    }
    public LateTask(Action action, float time, string name = "No Name Task", bool shoudLog = true)
    {
        this.action = action ?? throw new ArgumentNullException(nameof(action));
        this.timer = time;
        this.name = name;
        this.shouldLog = shoudLog;
        Tasks.Add(this);
        if (name != "")
            if (shoudLog)
            Logger.Info("\"" + name + "\" is created", "LateTask");
    }
    public static void Update(float deltaTime)
    {
        var TasksToRemove = new List<LateTask>();
        foreach (var task in Tasks.ToArray())
        {
            // An earlier callback may have cancelled pending tasks.
            if (!Tasks.Contains(task)) continue;
            try
            {
                if (task.Run(deltaTime))
                {
                    if (task.name != "")
                        if (task.shouldLog)
                            Logger.Info($"\"{task.name}\" is finished", "LateTask");
                    TasksToRemove.Add(task);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}  in \"{task.name}\"\n{ex.StackTrace}", "LateTask.Error", false);
                TasksToRemove.Add(task);
            }
        }
        TasksToRemove.ForEach(task => Tasks.Remove(task));
    }
}
