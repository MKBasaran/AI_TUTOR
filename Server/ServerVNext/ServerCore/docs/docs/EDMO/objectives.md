# EDMO objectives
Within the EDMO study session, it is necessary to have tasks for the participants. Not only do tasks give participants a goal to strive towards, but also give a metric for evaluating progression of the group to be used for AI related analytic.

The objectives are provided by [EDMO session plugins](plugins.md). They are also responsible for keeping track of the objective, marking its completion state. The session will then expose the objectives to participants of the EDMO study session.

## Objective group
An objective group describes a set of objectives that relate to a single overarching goal. They may also be grouped with no specific grouping criteria. The objectives may be ordered or unordered, depending on preference, but this should be communicated using a numbered prefix. (eg. "1. First get the milk")

Plugins can only provide objective groups, and not individual objectives. This is because there may be multiple objective providers, and it helps keep the source of the objectives explicit.

Plugins may choose to provide more than one objective group, maybe because it contains multiple higher-level objectives.

```cs
class EDMOObjectiveGroup
{
	public string Title { get; init; }
	public string? Description { get; init; }
	public EDMOObjective[] Objectives { get; init; }
}
```

## Objective
An objective describes an atomic task. The task may be implicitly dependent on other tasks, but the responsibility of enforcing dependency is left to the objective provider.

An objective can be marked as completed by the provider at any time, based on any criteria. Once marked as complete, an objective cannot be uncompleted, and any attempts to do so will be ignored; This is to avoid the completion criteria be considered "arbitrary" by participants. Upon completion, an event handler is invoked to inform subscribers about the state change. 

```cs
class EDMOObjective
{
	public string Title { get; init; }
	public string? Description { get; init; }

	public Action? ObjectiveCompleted {get;set;} // Event handler for subscribers to the objective

	private bool _completed;
	public bool Completed
	{
		get => _completed;
		set
		{
			if (!value) // An objective cannot be undone arbitrarily
				return;

			if(_completed) // Already completed
				return;

			_completed = value;
			ObjectiveCompleted?.Invoke();
		}
	}
}
```