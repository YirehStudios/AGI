# **AGI SYSTEM ARCHITECTURE MANUAL: STANDALONE CONFIGURATION SUBSYSTEM REFRACTOR (vE 0.1)**

This comprehensive technical manual defines the rigorous specifications for decoupling the Settings component from the primary MainApp runtime, transforming it into an independent, modular, data-driven sub-system. All implementations must adhere strictly to the Don't Repeat Yourself (DRY) programming philosophy, utilizing strict type architectures and clean separation of concerns within Godot 4.x Mono (C\#).

## **ESTHETIC DIRECTIVE & VISUAL PHILOSOPHY**

The interface design must strictly replicate a high-efficiency developer tool environment (e.g., Continue/VSCode minimalist configuration layouts). Cyberpunk, neon, or glow aesthetics are strictly prohibited. The system must prioritize high contrast, dense but legible hierarchies, clean spacing, and structural clarity. Visual changes must dynamically respect the unified global theme framework without overriding native system properties via hardcoded colors.

## **PHASE 1: SCENE RESOLUTION AND HIERARCHY DECOUPLING**

### **Role: Lead System Scene Architect**

**Objective:** Extract the legacy Settings panel logic from mainapp.tscn and establish a fully standalone, encapsulated scene titled Settings.tscn. Redefine the node structures using native Godot INI specifications to provide a modular 4-tab sidebar architecture with content display viewports.

### **Target Structural Modifications:**

| Node Name | Type | Configuration Path / Constraints |
| :---- | :---- | :---- |
| **SettingsRoot** | PanelContainer | Root node of Settings.tscn. Unique ID enabled. Full Rect anchoring. Uses generic panel StyleBoxFlat from global theme. |
| **HBoxLayout** | HBoxContainer | Primary horizontal split layer. Separation constant set to 20px. |
| **NavigationSidebar** | VBoxContainer | Left-hand alignment. Custom minimum Width: 240px. Contains module selection buttons. |
| **ContentPanel** | PanelContainer | Right-hand alignment. Size flags: Expand & Fill. Displays active module content via dynamically swapped containers. |

### **Navigation Component Definitions:**

The NavigationSidebar node must contain exactly four structural button instances to toggle control views:

1. **ModelsBtn:** Text: "Modelos". Toggles LLM, embedding, and vision configuration arrays.  
2. **PerformanceBtn:** Text: "Rendimiento". Replaces legacy rules panels with absolute hardware allocation arrays.  
3. **ToolsBtn:** Text: "Herramientas (MCP)". Renders the Model Context Protocol granular access configuration grid.  
4. **PrivacyBtn:** Text: "Privacidad y Datos". Replaces placeholder help components with sovereign datastore configurations.

### **Validation Checkpoint Alpha:**

Verify that instances of Settings.tscn can be cleanly added as children to the SettingsOverlay wrapper node within mainapp.tscn without breaking UI layouts or generating anchor propagation errors in the Godot debugger console.

## **PHASE 2: STANDALONE SCRIPT CONTROLLER IMPLEMENTATION**

### **Role: Principal Core C\# Software Engineer**

**Objective:** Re-engineer Settings.cs to manage all state manipulations natively, intercept inputs from the newly engineered Settings.tscn sub-scene, and coordinate reactive data transformations with ConfigManager, BackendLauncher, and NetworkManager.

### **C\# Code Architecture Specification:**

Implement the standalone controller class structure using strict naming boundaries and decoupled signal binding patterns:  
`using Godot;`  
`using System;`  
`using System.Collections.Generic;`  
`using Logic.System.Config;`  
`using Logic.Backend;`  
`using Logic.Network;`

`namespace Logic.UI`  
`{`  
    `public partial class Settings : PanelContainer`  
    `{`  
        `[ExportGroup("Navigation Nodes")]`  
        `[Export] public Button ModelsBtn { get; set; }`  
        `[Export] public Button PerformanceBtn { get; set; }`  
        `[Export] public Button ToolsBtn { get; set; }`  
        `[Export] public Button PrivacyBtn { get; set; }`

        `[ExportGroup("View Containers")]`  
        `[Export] public Container ModelsViewContainer { get; set; }`  
        `[Export] public Container PerformanceViewContainer { get; set; }`  
        `[Export] public Container ToolsViewContainer { get; set; }`  
        `[Export] public Container PrivacyViewContainer { get; set; }`

        `private ConfigManager _configManager;`  
        `private BackendLauncher _backendLauncher;`  
        `private NetworkManager _networkManager;`

        `public override void _Ready()`  
        `{`  
            `InitializeSystemLinks();`  
            `BindNavigationSignals();`  
            `LoadActiveConfiguration();`  
        `}`

        `private void InitializeSystemLinks()`  
        `{`  
            `_configManager = GetNodeOrNull<ConfigManager>("/root/ConfigManager");`  
            `_backendLauncher = GetNodeOrNull<BackendLauncher>("/root/BackendLauncher");`  
            `_networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");`

            `if (_configManager == null || _backendLauncher == null || _networkManager == null)`  
            `{`  
                `GD.PrintErr("[SETTINGS ERROR] Dependency linkage initialization failed.");`  
            `}`  
        `}`

        `private void BindNavigationSignals()`  
        `{`  
            `ModelsBtn.Pressed += () => SwitchActiveView(0);`  
            `PerformanceBtn.Pressed += () => SwitchActiveView(1);`  
            `ToolsBtn.Pressed += () => SwitchActiveView(2);`  
            `PrivacyBtn.Pressed += () => SwitchActiveView(3);`  
        `}`

        `private void SwitchActiveView(int viewIndex)`  
        `{`  
            `ModelsViewContainer.Visible = (viewIndex == 0);`  
            `PerformanceViewContainer.Visible = (viewIndex == 1);`  
            `ToolsViewContainer.Visible = (viewIndex == 2);`  
            `PrivacyViewContainer.Visible = (viewIndex == 3);`  
        `}`

        `private void LoadActiveConfiguration()`  
        `{`  
            `if (_configManager?.ActiveProfile == null) return;`  
            `// Concrete configuration parameters initialization logic populates here dynamically.`  
        `}`  
    `}`  
`}`

### **Validation Checkpoint Beta:**

Compile the solution assembly. Confirm that no cross-reference leaks exist between MainApp.cs and Settings.cs. MainApp must handle toggle visualization only, leaving configuration state lifecycle management exclusively to Settings.

## **PHASE 3: CONCRETE FUNCTIONAL MODULE DEVELOPMENT**

### **Role: Full-Stack Integration Engineer**

**Objective:** Construct and map the internal functional logic for Module 1 (Models) and Module 2 (Performance) within their respective UI containers, ensuring absolute alignment with data structures and dynamic backend updates.

### **Module 1: Language Model Configuration (ModelsViewContainer)**

The view container layout must be clean and minimal, containing fields mapped to parameters inside the ModelProfile configuration architecture:

* **Active Model Identifier Selector:** An OptionButton populated by searching user://models/ for target profile JSON configurations. Swapping items triggers runtime profile deserialization and assigns structural updates directly to \_configManager.ActiveProfile.  
* **Context Horizon Allocation Field:** A SpinBox targeting context limit manipulation. Range boundaries: Min 512, Max 131072 tokens. Mapped properties update \_configManager.ActiveProfile.Template context ceilings.  
* **Weight Import Pipeline:** A functional LineEdit alongside an import trigger Button to fetch external model resources or specify external globalized absolute filesystem locations safely.

### **Module 2: Compute Boundary Manipulation (PerformanceViewContainer)**

Controls structural properties mapped to execution arguments used during local runtime instantiations:

* **CPU Execution Threads Boundary Allocation:** A SpinBox node mapping hardware utilization threads. Range limits: Min 1, Max total logical CPU processor bounds of host hardware environment. Writes values directly to the platform thread state configuration attributes.  
* **GPU Offload Compute Boundary Layer Matrix:** A numerical SpinBox to increment layers allocated to VRAM processing blocks. Range bounds: 0 to 120 layers. If configured \> 0, system applies target platform parameters to compilation flags (e.g., \-ngl arguments sent during engine instantiation).  
* **RAM Saturation Safety Guard Ceiling:** A boundary constraint field defining memory thresholds. Prevents the execution thread layers from spawning allocations exceeding local operating boundaries.

### **Live-Updating Integration Framework Strategy (DRY Execution):**

When values are committed via these interactive boundary inputs, the application must run unified update sequences without replicating state modifications across different scripts. Modifications trigger configuration persistence blocks followed by explicit hot-restarts of background engines if the execution environment is currently defined as a localized host state.

| Target Element | Interactive Control Component Type | Target Backend Property Destination Path |
| :---- | :---- | :---- |
| Active Model File | OptionButton | \_configManager.ActiveProfile.ModelId |
| Context Limit Bounds | SpinBox | \_configManager.ActiveProfile.Template.ContextCeiling |
| Hardware Threads Count | SpinBox | \_configManager.PerformanceProfile.CpuThreads |
| GPU Layers Shifting | SpinBox | \_configManager.PerformanceProfile.GpuLayers |

### **Validation Checkpoint Gamma:**

Verify that altering local performance sliders updates configuration files on disk instantly and triggers explicit process termination sequences followed by systematic restarts within BackendLauncher.cs when operating in LocalHost state.

## **PHASE 4: GRANULAR SECURITY ACCESS AND DATA SOVEREIGNTY SCHEMAS**

### **Role: Systems Infrastructure Security Engineer**

**Objective:** Build the operational mechanics for Module 3 (Model Context Protocol Gateway Permissions) and Module 4 (Sovereign Data Storage Boundaries), ensuring secure data isolation and programmatic integration.

### **Module 3: Protocol Gateway Permissions Matrix (ToolsViewContainer)**

Provides strict access control filtering arrays over tools registered in the microservices configuration layers:

* **Interactive Registry Grid Element:** A structural list mapping active tools returned by the MCP synchronization endpoint (e.g., web\_search, os\_command, edit\_existing\_file).  
* **Granular Authority Policy Selection Grid:** Each row maps exactly to an OptionButton dropdown providing three authority options:  
  1. **Automatic:** System allows execution matching tool signatures instantly without pausing interface layers.  
  2. **Ask First:** Blocks transaction routing pipeline, triggering the UI request confirmation workflow overlay.  
  3. **Excluded:** Forcibly filters the tool signature from the available tools schemas schema payloads completely, making it invisible to the LLM context.

### **Module 4: Sovereign Local Datastore Manipulation (PrivacyViewContainer)**

Exposes storage rules to ensure absolute local user data control:

* **Absolute Log Storage Target Directive Path:** A text input field (LineEdit) displaying the location of localized operational context files. Path target default root starts at user://history/ or \~/.local/share/agi/workspace.  
* **Purge Execution Pipeline Trigger:** A high-priority Button wired to delete localized session indices and clean SQLite database tables instantly.  
* **Telemetry Isolation Switch:** A clean CheckButton toggle to sever external payload tracking or metrics reporting vectors entirely, forcing absolute network isolation.

### **Validation Checkpoint Delta:**

Verify that tools set to Excluded are correctly dropped from the JSON payload when ChatManager.cs calls BuildPrompt() and references the \_availableTools data structure block.

## **PHASE 5: SYSTEM INTEGRATION & VERIFICATION PIPELINE**

### **Role: Lead Software Quality & Execution Engineer**

**Objective:** Coordinate execution flow testing across all modified scripts and scenes. Ensure execution transitions smoothly between Local Host and Cloud API execution frames without leaking resource processes or producing thread synchronization stalls.

### **Execution Verification Tasks Matrix:**

1. **Clean Preference Deserialization Pass:** Launch with an existing valid active configuration file. Confirm the standalone Settings subsystem loads structural properties smoothly without prompting fallback setup operations.  
2. **Runtime Hot-Swapping Stability Pass:** Alter context bounds dynamically while streaming an active LLM completion connection. Confirm that memory handles remain intact and completion token emission streams continue without dropping data frames.  
3. **Orphaned Resource Termination Pass:** Transition state parameters directly from Local Host mode to Cloud API mode. Inspect host platform resource management layers. Confirm that background instances of processing structures like llama-server are cleanly terminated.

## **POST-EXECUTION REPORT FORMAT REQUIREMENT**

Upon completion of each phase, the execution AI must provide a concise technical report structured exactly according to the following template framework:  
`=================== AGI DECOUPLING EXECUTION REPORT ===================`  
`PHASE EXECUTED: [Phase Index and Identifier Label]`  
`SCENE MODIFICATIONS COMPLETED:`  
`- [Absolute Path to Target TSCN File] -> [Detailed description of node hierarchy shifts completed]`

`SCRIPT CONSOLIDATIONS COMPLETED:`  
`- [Target Class/Namespace Identifier Name] -> [Summary list of code modifications implemented]`

`CHECKPOINT VERIFICATION METRICS:`  
`- [Checkpoint Name] -> STATUS: [PASS/FAIL] | Latency Delta: [ms] | State Alignment: [Verified/Unverified]`

`ARCHITECTURAL ANOMALIES ENCOUNTERED & SOLUTIONS APPLIED:`  
`- [Describe any compilation errors resolved or layout anomalies addressed]`  
`=======================================================================`  
