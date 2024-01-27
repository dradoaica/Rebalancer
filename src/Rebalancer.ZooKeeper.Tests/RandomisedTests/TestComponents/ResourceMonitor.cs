using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

public class ResourceMonitor
{
    private readonly ConcurrentQueue<AssignmentEvent> assignmentEvents;
    private readonly HashSet<string> removedResources;
    private readonly Dictionary<string, string> resources;
    private readonly List<object> violations;

    public ResourceMonitor()
    {
        resources = new Dictionary<string, string>();
        violations = new List<object>();
        assignmentEvents = new ConcurrentQueue<AssignmentEvent>();
        removedResources = new HashSet<string>();
    }

    public void CreateResource(string resourceName)
    {
        resources.Add(resourceName, "");
    }

    public List<object> GetDoubleAssignments()
    {
        return violations;
    }

    public bool DoubleAssignmentsExist()
    {
        return violations.Any();
    }

    public bool AllResourcesAssigned()
    {
        var unassigned = resources.Where(x => x.Value == string.Empty).ToList();
        if (unassigned.Any())
        {
            Console.WriteLine(
                $"{DateTime.Now.ToString("hh:mm:ss,fff")}Unassigned resources: {string.Join(",", unassigned)}");
            return false;
        }

        return true;
    }

    public void Clear()
    {
        resources.Clear();
    }

    public void ClaimResource(string resourceName, string clientId)
    {
        assignmentEvents.Enqueue(new AssignmentEvent
        {
            EventTime = DateTime.Now, ClientId = clientId, Action = $"Assign {resourceName}"
        });
        if (resources.ContainsKey(resourceName))
        {
            var currValue = resources[resourceName];
            if (currValue.Equals(string.Empty))
            {
                resources[resourceName] = clientId;
            }
            else
            {
                ClaimViolation violation = new(resourceName, currValue, clientId);
                assignmentEvents.Enqueue(new AssignmentEvent
                {
                    ClientId = clientId, Action = violation.ToString(), EventTime = DateTime.Now
                });
                violations.Add(violation);
            }
        }
    }

    public void ReleaseResource(string resourceName, string clientId)
    {
        assignmentEvents.Enqueue(new AssignmentEvent
        {
            EventTime = DateTime.Now, ClientId = clientId, Action = $"Release {resourceName}"
        });

        if (resources.ContainsKey(resourceName))
        {
            var currValue = resources[resourceName];
            if (currValue.Equals(clientId))
            {
                resources[resourceName] = string.Empty;
            }
            else if (currValue.Equals(string.Empty))
            {
                // fine
            }
            else
            {
                ReleaseViolation violation = new(resourceName, currValue, clientId);
                assignmentEvents.Enqueue(new AssignmentEvent
                {
                    ClientId = clientId, Action = violation.ToString(), EventTime = DateTime.Now
                });
                violations.Add(violation);
            }
        }
    }

    public void AddResource(string resourceName)
    {
        resources.Add(resourceName, string.Empty);
        assignmentEvents.Enqueue(new AssignmentEvent
        {
            EventTime = DateTime.Now, ClientId = "-", Action = $"Add Resource - {resourceName}"
        });
    }

    public void RemoveResource(string resourceName)
    {
        resources.Remove(resourceName);
        removedResources.Add(resourceName);
        assignmentEvents.Enqueue(new AssignmentEvent
        {
            EventTime = DateTime.Now, ClientId = "-", Action = $"Remove Resource - {resourceName}"
        });
    }

    public void RegisterAddClient(string clientId)
    {
        assignmentEvents.Enqueue(new AssignmentEvent
        {
            EventTime = DateTime.Now, ClientId = "-", Action = $"Add Client - {clientId}"
        });
    }

    public void RegisterRemoveClient(string clientId)
    {
        assignmentEvents.Enqueue(new AssignmentEvent
        {
            EventTime = DateTime.Now, ClientId = "-", Action = $"Remove Client - {clientId}"
        });
    }

    public void PrintEvents(string path)
    {
        List<string> lines = new();

        while (true)
        {
            if (assignmentEvents.TryDequeue(out var evnt))
            {
                lines.Add($"{evnt.EventTime.ToString("hh:mm:ss,fff")}|{evnt.ClientId}|{evnt.Action}");
            }
            else
            {
                break;
            }
        }

        List<KeyValuePair<string, string>> resList = new(resources.ToList());
        lines.Add("||---- Resource Assignment State -----");
        foreach (var kv in resList)
        {
            lines.Add($"||{kv.Key}->{kv.Value}");
        }

        lines.Add("||------------------------------------");

        if (!File.Exists(path))
        {
            File.WriteAllText(path, "Time|Client|Action" + Environment.NewLine);
        }

        File.AppendAllLines(path, lines);
    }
}
