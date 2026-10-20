# Security and behavior

This source requests PROCESS_VM_READ and PROCESS_QUERY_INFORMATION for CS2.
It does not request process write, allocation or thread-creation rights.

The application has no DLL injector, kernel driver, memory writer, aim control,
anti-cheat bypass, startup registration, credentials collection or data upload.
It downloads only JSON data from api.github.com and raw.githubusercontent.com.
GitHub receives normal HTTPS request metadata such as IP address and User-Agent.
No executable or script is downloaded or executed by the application.

The application runs with the launching user's privileges. It reports denied
access rather than trying to bypass it. Its two global hotkeys are registered
with Windows; it does not install a keyboard hook or record typed text.

The Windows checks use only the runner's own test process, not another user's
game session. They are functional smoke checks, not malware certification.
The build is unsigned. Source review and a successful build cannot guarantee
anti-cheat compatibility or prove absence of every security defect.
