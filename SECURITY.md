# Security Policy

## Supported Versions
We are committed to the security of our users and the integrity of the AGI ecosystem. We actively provide security updates for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0.0 | :x:                |

## Security Philosophy
The AGI ecosystem is built on a **Zero Telemetry** architecture. This fundamental design ensures that no sensitive data, personal information, or academic records ever leave the local environment. By processing all information on-premise, we eliminate the risk of data exfiltration to third-party servers.

Our security framework focuses on two primary areas:
* **Core Integrity:** Protecting the native C# core to ensure the stability of the local processing engine.
* **Inference Isolation:** Ensuring that local Artificial Intelligence models remain isolated from unauthorized external network access, maintaining a strictly private environment.

## Reporting a Vulnerability
If you identify a security vulnerability that could compromise the Zero Telemetry architecture or the integrity of the local hardware, please do not disclose it publicly. Reporting vulnerabilities privately allows us to address them without exposing institutional data to potential risks.

To report a vulnerability, please follow these steps:
1. Send a detailed email to: **[irvingyahirhernandezmarinmx@gmail.com]**
2. Include a description of the vulnerability and the specific hardware or software environment where it was identified.
3. Provide steps or a script to reproduce the issue if possible.

Our technical team will acknowledge your report within 72 hours and provide an estimated timeline for a resolution.

## Responsible Disclosure Policy
We follow a responsible disclosure model. We ask that you give our team a reasonable amount of time to resolve the issue before making any information public. This collaboration ensures the safety of the students and teachers using the platform in public and private institutions.

## Non-Security Issues
For general bugs, user interface glitches, or performance optimization requests that do not pose a security risk, please open a standard issue in the **GitHub Issues** tab of this repository.