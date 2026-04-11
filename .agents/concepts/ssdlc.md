# Secure Software Development Life Cycle (SSDLC)

## Creating an SSDLC framework: Secure software development life cycle processes

Because every organization’s software development and deployment processes are different, approaches to implementing an
SSDLC will vary.

In general, however, establishing a secure SDLC starts with creating an SSDLC framework that identifies the following:

- Which security policies and best practices developers should follow when designing and implementing applications.
- Which types of security tests or scans the organization performs during the development process.
- When those tests and scans take place during the SDLC.
- How developers react to tests and scan results. For example, which types of security risks mean that the application
  development process needs to pause until developers fix the issues?
- How developers, IT operations engineers, and security experts communicate and collaborate to ensure everyone has
  visibility into the state of the SSDLC.

For best practices on how to define secure software development life cycle processes, **OWASP SAMM**, an open framework
to help businesses manage cybersecurity risks, is helpful. In particular, consider the guidance in
the [SAMM Implementation Model](https://owaspsamm.org/model/implementation/), which covers software development and
deployment processes.

[NIST SSDF](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-218.pdf) (which is based in part on OWASP
guidance) is also relevant, especially for examples of implementation of secure processes at various stages of the SDLC.

---

## SSDLC phases

Once you have an SSDLC framework, you can use it to drive the integration of security into all relevant stages of the
SDLC. Again, the exact process will vary from one organization to the next, but key phases of the SSDLC typically
include:

- **Planning**  
  During the planning stage, which involves defining the overall goals and structure of a software project, developers
  should ensure that security is a priority and that adequate resources are available within the project to support it.

- **Design**  
  When developers design an application, they should assess potential security risks and ensure that the design
  mitigates them.

- **Coding**  
  As they write code, developers should adhere to best practices for secure coding. They should also consider running
  security tests on newly written code as a way of catching some security issues early.

- **Testing**  
  The testing phase of the SDLC typically happens after all new code has been written and compiled and the application
  is deployed in a dev/testing environment. This is another opportunity to perform tests, even if earlier testing of
  source code already happened.

- **Maintenance**  
  Once the SDLC reaches the maintenance stage, meaning that the application has been deployed, security testing and
  monitoring should continue to discover risks that may not have been evident from testing in a dev/test environment.

---

## SSDLC vs. SDLC

When implemented properly, an SSDLC includes several key features that don’t factor into a standard SDLC.

| Aspect                    | SDLC                                                   | SSDLC                                                                        |
|---------------------------|--------------------------------------------------------|------------------------------------------------------------------------------|
| **Security focus**        | Usually addressed later.                               | Embedded from the planning stage.                                            |
| **Main goal(s)**          | Develop functional software.                           | Develop functional and secure software.                                      |
| **Cost of fixing issues** | Higher if security flaws are found late.               | Lower due to early detection.                                                |
| **Approach**              | Reactive; security issues are detected after the fact. | Proactive because security is integrated throughout the development process. |
| **Testing focus**         | Testing application functionality and performance.     | Testing application functionality, performance, and security.                |

---

## SSDLC best practices

Declaring security a priority and integrating security across all key stages of the SDLC is a start. To get the most
from the SSDLC concept, consider the following best practices:

- **Define clear security policies and procedures**  
  Policies and procedures serve as the foundation that everyone in the organization can refer to when deciding which
  security practices and tests to implement during the SDLC.

- **Delegate responsibilities**  
  To ensure that engineers react quickly to issues identified during security testing, make clear who is responsible for
  what. Otherwise, security problems may go unresolved.

- **Automate security testing**  
  Automating tests and scans is critical for ensuring that the secure SDLC remains efficient and that testing does not
  delay application releases.

- **Set priority levels**  
  Not every security issue is serious enough to delay application delivery. By assigning priority levels to
  vulnerabilities, teams can establish clear response guidelines.

---

## Documentation

- [What Is the Secure Software Development Lifecycle (SSDLC)?](https://www.aquasec.com/cloud-native-academy/supply-chain-security/secure-software-development-lifecycle-ssdlc/)
- [Microsoft Security Development Lifecycle Practices](https://www.microsoft.com/en-us/securityengineering/sdl/practices)
