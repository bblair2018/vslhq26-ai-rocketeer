# vslhq26-ai-rocketeer

**Team:** Solo — [@bblair2018](https://github.com/bblair2018)

**Category:** Primary — Best AI Agent or Workflow Automation · Secondary — Best Azure OpenAI / LLM-Powered App

## What It Does

Jira Rollup Agent summarizes Jira activity across the full ticket hierarchy — Initiative → Epic → Story/Bug/Task/Spike (with Subtasks and StoryBugs under Stories). Comments (with author, timestamp, and role) are summarized at the item level and rolled up into a single HTML report: Initiatives are listed in order of business priority/rank, each showing a high-level **Business Summary** (weighted toward PM/Scrum Master/Stakeholder commentary: status, risk, business impact), with each Initiative's Epics nested beneath it showing an **Engineering Summary** (weighted toward Dev/QA commentary: technical progress, blockers, bugs).

## Architecture

**Ticket Hierarchy:**

- Initiative
  - Epic
    - Story
      - Subtask
      - StoryBug
    - Bug (standalone, same level as Story)
    - Task
    - Spike

**Report Structure (single HTML report):**

- Initiatives, ordered by business priority/rank (from mock data)
  - Business Summary (per Initiative)
  - Epics (per Initiative)
    - Engineering Summary (per Epic)

**Data Flow:**

Mocked Jira Hierarchy (above, includes priority/rank field on Initiatives) → .NET Agent → Azure OpenAI → Item Summaries → Epic Engineering Summaries → Initiative Business Summaries → Sorted by Priority → Single HTML Report

1. **Input**: Mocked sample data representing the Jira hierarchy — Initiatives (with a priority/rank field) containing Epics, which contain Stories, Bugs, Tasks, and Spikes; Stories additionally contain Subtasks and StoryBugs. All items carry comments with author, timestamp, and author role (Dev/QA/PM/Scrum Master/Stakeholder).
2. **Item summarization**: Single LLM summary per Story, Bug, Task, Spike (rolling up their Subtasks/StoryBugs where applicable)
3. **Epic summarization**: Comments across each Epic's items, filtered/weighted toward Dev/QA roles, summarized into an Engineering Summary
4. **Initiative summarization**: Comments across the Initiative, filtered/weighted toward PM/Scrum Master/Stakeholder roles, summarized into a Business Summary
5. **Sorting**: Initiatives ordered by their priority/rank field
6. **Output**: A single HTML report — Initiatives listed by business priority, each with its Business Summary and nested Epic Engineering Summaries

*Note: mock data is used to simulate the existing Jira ingestion pipeline (already built separately) so the hackathon build can focus on summarization and reporting.*

## Tech Stack

- .NET 10 / C#
- Azure OpenAI (GPT-4o) — fallback: GitHub Models
- Mocked Jira hierarchy data (JSON) standing in for the existing ingestion pipeline

## Getting Started

TBD — setup instructions will be added once the project is built at the event.

## Demo

📹 `./demo/demo.mp4`

## Known Limits

- Uses mocked Jira data rather than a live pull for the demo; real ingestion pipeline exists separately and can be substituted
- Item-level summaries (Story/Bug/Task/Spike) do not have a role split — role weighting only applies at Epic (Engineering) and Initiative (Business) level
- Initiative ranking relies on a priority/rank field present in the mock data — no independent prioritization logic
- Summary quality depends on comment volume and consistency of the mocked/real role data
