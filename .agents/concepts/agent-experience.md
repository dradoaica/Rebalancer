# Agent Experience (AgentEx) & Context Management

Agent Experience (AgentEx) focuses on optimizing the environment in which agents operate to maximize reliability, reduce
hallucination, and ensure efficient execution.

## Context Management

Context is the most precious resource in an agentic workflow. "Context Pollution" occurs when the agent's context window
is filled with irrelevant, redundant, or outdated information, leading to degraded performance.

### Guidelines for Context Pruning

1. **Relevance Filtering**: Before providing a large file or dataset to an agent, prune it to the sections relevant to
   the task.
2. **Summarization**: Use intermediate agents to summarize long discussions or logs before passing them to the primary
   worker agent.
3. **Hierarchy of Information**: Provide high-level structure first, and only drill down into details upon request or
   when specifically needed for the current step.
4. **State Persistence**: Agents should maintain a concise `status` or `context` file for long-running tasks to track
   progress across sessions without re-reading the entire history.

## Preventing Context Pollution

- **Atomic Tasks**: Break large goals into small, atomic tasks that require minimal context.
- **Explicit Scoping**: Clearly define the scope of the current operation to prevent the agent from wandering into
  unrelated areas of the codebase.
- **Regular Resets**: For chat-based agents, periodically start a fresh session with only the necessary state carried
  over.

## Human-in-the-Loop (HITL) Role

Humans act as "Context Curators." If an agent begins to hallucinate or struggle, the human should intervene not just to
fix the code, but to prune the agent's context and redirect its focus.
