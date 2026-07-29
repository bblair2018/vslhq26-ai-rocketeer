/*
 @licstart  The following is the entire license notice for the JavaScript code in this file.

 The MIT License (MIT)

 Copyright (C) 1997-2020 by Dimitri van Heesch

 Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 and associated documentation files (the "Software"), to deal in the Software without restriction,
 including without limitation the rights to use, copy, modify, merge, publish, distribute,
 sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

 The above copyright notice and this permission notice shall be included in all copies or
 substantial portions of the Software.

 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
 BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

 @licend  The above is the entire license notice for the JavaScript code in this file
*/
var NAVTREE =
[
  [ "Jira Rollup Agent", "index.html", [
    [ "vslhq26-ai-rocketeer", "index.html", "index" ],
    [ "CLAUDE.md", "md__c_l_a_u_d_e.html", [
      [ "What this is", "md__c_l_a_u_d_e.html#autotoc_md8", null ],
      [ "Commands", "md__c_l_a_u_d_e.html#autotoc_md9", null ],
      [ "Architecture", "md__c_l_a_u_d_e.html#autotoc_md10", [
        [ "Ticket hierarchy", "md__c_l_a_u_d_e.html#autotoc_md11", null ],
        [ "Intended data flow (implemented)", "md__c_l_a_u_d_e.html#autotoc_md12", [
          [ "Role-weighting is soft (emphasis), not a hard filter", "md__c_l_a_u_d_e.html#autotoc_md13", null ],
          [ "Item→Epic→Initiative summary chaining (implemented)", "md__c_l_a_u_d_e.html#autotoc_md14", null ],
          [ "The three prompt types, and their exact system prompts (implemented)", "md__c_l_a_u_d_e.html#autotoc_md15", null ],
          [ "Summary storage schema (implemented)", "md__c_l_a_u_d_e.html#autotoc_md16", null ],
          [ "Date-range filtering for summarization (implemented)", "md__c_l_a_u_d_e.html#autotoc_md17", null ],
          [ "Implementation order for <span class=\"tt\">SummarizationService</span> (completed)", "md__c_l_a_u_d_e.html#autotoc_md18", null ]
        ] ],
        [ "<span class=\"tt\">src/JiraRollupAgent/MockData/</span>", "md__c_l_a_u_d_e.html#autotoc_md19", [
          [ "<span class=\"tt\">jira-hierarchy.json</span> (~5100 lines — the primary pipeline input)", "md__c_l_a_u_d_e.html#autotoc_md20", null ],
          [ "<span class=\"tt\">issue-type-workflows.json</span>", "md__c_l_a_u_d_e.html#autotoc_md21", null ],
          [ "<span class=\"tt\">team-roster.json</span>", "md__c_l_a_u_d_e.html#autotoc_md22", null ]
        ] ],
        [ "App boilerplate (adapted from <span class=\"tt\">C:\\UATP_CODE\\INSYTE\\StatusReporter\\StatusReporter.Console</span>)", "md__c_l_a_u_d_e.html#autotoc_md23", null ],
        [ "<span class=\"tt\">Services/</span> — pipeline stages", "md__c_l_a_u_d_e.html#autotoc_md24", [
          [ "Load-once behavior (all three stages)", "md__c_l_a_u_d_e.html#autotoc_md25", null ],
          [ "<span class=\"tt\">Models/JiraHierarchyLoaderService/MockDataModels.cs</span>", "md__c_l_a_u_d_e.html#autotoc_md26", null ]
        ] ],
        [ "<span class=\"tt\">DAL/</span> — data access layer", "md__c_l_a_u_d_e.html#autotoc_md27", [
          [ "Table schema (verified against <span class=\"tt\">VSLiveJiraRollup</span>)", "md__c_l_a_u_d_e.html#autotoc_md28", null ],
          [ "Current data volume (verified against <span class=\"tt\">VSLiveJiraRollup</span>)", "md__c_l_a_u_d_e.html#autotoc_md29", null ]
        ] ],
        [ "Known limits (from README, still applicable to any implementation)", "md__c_l_a_u_d_e.html#autotoc_md30", null ]
      ] ]
    ] ],
    [ "Jira Rollup Agent — Hierarchy, Reporting Structure, and Prompt Types", "md_prompt-types-overview.html", [
      [ "1. The full ticket hierarchy", "md_prompt-types-overview.html#autotoc_md32", null ],
      [ "2. What the final report actually shows", "md_prompt-types-overview.html#autotoc_md33", null ],
      [ "3. The three generic prompt types", "md_prompt-types-overview.html#autotoc_md34", [
        [ "Type A — Leaf summary (no children, no weighting)", "md_prompt-types-overview.html#autotoc_md35", null ],
        [ "Type B — Rollup summary, no weighting", "md_prompt-types-overview.html#autotoc_md38", null ],
        [ "Type C — Rollup summary, role-weighted (one template, two parameterizations)", "md_prompt-types-overview.html#autotoc_md40", null ]
      ] ],
      [ "4. Tying it together", "md_prompt-types-overview.html#autotoc_md43", null ],
      [ "5. End-to-end walkthrough: one Initiative, one example of each type", "md_prompt-types-overview.html#autotoc_md44", [
        [ "Step 1 — Subtask SUB-PFD-1-1-1 (Type A)", "md_prompt-types-overview.html#autotoc_md45", null ],
        [ "Step 2 — StoryBug SBUG-PFD-1-1-1 (Type A)", "md_prompt-types-overview.html#autotoc_md46", null ],
        [ "Step 3 — Story STORY-PFD-1-1 (Type B, consumes Steps 1 &amp; 2)", "md_prompt-types-overview.html#autotoc_md47", null ],
        [ "Step 4 — Bug BUG-PFD-1-3 (Type A, standalone — sibling of the Story, not nested under it)", "md_prompt-types-overview.html#autotoc_md48", null ],
        [ "Step 5 — Task TASK-PFD-1-4 (Type A, standalone)", "md_prompt-types-overview.html#autotoc_md49", null ],
        [ "Step 6 — Spike SPIKE-PFD-1-5 (Type A, standalone)", "md_prompt-types-overview.html#autotoc_md50", null ],
        [ "Step 7 — Epic EPIC-PFD-1 (Type C, Dev/QA-weighted, consumes Steps 3-6)", "md_prompt-types-overview.html#autotoc_md51", null ],
        [ "Step 8 — Initiative INIT-PFD (Type C, ScrumMaster/Stakeholder/EM-weighted, consumes Step 7)", "md_prompt-types-overview.html#autotoc_md52", null ]
      ] ]
    ] ],
    [ "Namespaces", "namespaces.html", [
      [ "Namespace List", "namespaces.html", "namespaces_dup" ]
    ] ],
    [ "Classes", "annotated.html", [
      [ "Class List", "annotated.html", "annotated_dup" ],
      [ "Class Index", "classes.html", null ],
      [ "Class Hierarchy", "hierarchy.html", "hierarchy" ],
      [ "Class Members", "functions.html", [
        [ "All", "functions.html", "functions_dup" ],
        [ "Functions", "functions_func.html", null ],
        [ "Variables", "functions_vars.html", null ],
        [ "Properties", "functions_prop.html", null ]
      ] ]
    ] ],
    [ "Files", "files.html", [
      [ "File List", "files.html", "files_dup" ],
      [ "File Members", "globals.html", [
        [ "All", "globals.html", null ],
        [ "Typedefs", "globals_type.html", null ]
      ] ]
    ] ]
  ] ]
];

var NAVTREEINDEX =
[
"_html_report_generator_service_8cs.html",
"class_jira_rollup_agent_1_1_d_a_l_1_1_repositories_1_1_implementations_1_1_unit_of_work.html#a9c015d0c9c6983d87312279d4ddaf84f",
"class_jira_rollup_agent_1_1_services_1_1_summarization_service_1_1_summarization_service.html#ac0d749c8afba4fd8be04600a5f7cc69c"
];

const SYNCONMSG = 'click to disable panel synchronization';
const SYNCOFFMSG = 'click to enable panel synchronization';
const LISTOFALLMEMBERS = 'List of all members';