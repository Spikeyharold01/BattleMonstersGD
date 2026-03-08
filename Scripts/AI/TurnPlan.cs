using System.Collections.Generic;
using System.Linq;
using System.Text;
// =================================================================================================
// FILE: TurnPlan.cs (GODOT VERSION)
// PURPOSE: Represents a sequence of actions an AI intends to take.
// ATTACH TO: Do not attach (Pure C# Class).
// =================================================================================================
public class TurnPlan
{
public List<AIAction> Actions { get; private set; }
public float Score { get; set; }
public string Name { get; private set; }
public bool IsPlayerSuggestion { get; set; } = false;
public string DecisionNarrative { get; set; } = string.Empty;

public TurnPlan()
{
    Actions = new List<AIAction>();
    Score = 0;
    Name = "Do Nothing";
}

public TurnPlan(List<AIAction> actions)
{
    Actions = actions;
	    Score = 0;
    UpdateName();
}

public TurnPlan(AIAction action)
{
    Actions = new List<AIAction> { action };
	    Score = 0;
    UpdateName();
}

public void UpdateName()
{
    if (Actions == null || !Actions.Any())
    {
        Name = "Do Nothing";
        return;
    }

    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < Actions.Count; i++)
    {
        sb.Append(Actions[i].Name);
        if (i < Actions.Count - 1)
        {
            sb.Append(" -> ");
        }
    }
    Name = sb.ToString();
}
}