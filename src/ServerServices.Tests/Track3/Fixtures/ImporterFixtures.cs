namespace ServerServices.Tests.Track3.Fixtures;

/// <summary>
/// Minimal but realistic scanner reports, held as strings rather than files.
///
/// Inline because they have to stay in step with the importers they exercise, and a fixture in a
/// separate content file drifts silently — a reader of the test cannot see what it is parsing. Each
/// fixture is trimmed to the fields the importer reads plus at least one malformed record, because
/// an importer that silently drops rows is the failure mode these tests exist to catch.
/// </summary>
public static class ImporterFixtures
{
    /// <summary>
    /// Two hosts, four report items: one critical, one informational (filtered by default), one with
    /// no plugin name (skipped with a warning), and one on a second host.
    /// </summary>
    public const string Nessus = """
        <?xml version="1.0" encoding="UTF-8"?>
        <NessusClientData_v2>
          <Report name="scan">
            <ReportHost name="10.0.0.1">
              <HostProperties>
                <tag name="host-ip">10.0.0.1</tag>
                <tag name="host-fqdn">web.example.com</tag>
                <tag name="operating-system">Linux Kernel 5.4</tag>
                <tag name="mac-address">00:11:22:33:44:55</tag>
                <tag name="HOST_END_TIMESTAMP">1700000000</tag>
              </HostProperties>
              <ReportItem port="443" svc_name="https" protocol="tcp" severity="4"
                          pluginID="12345" pluginName="OpenSSL Heartbleed" pluginFamily="General">
                <description>The remote service is affected by Heartbleed.</description>
                <solution>Upgrade OpenSSL.</solution>
                <risk_factor>Critical</risk_factor>
                <plugin_output>TLS handshake revealed memory.</plugin_output>
                <cve>CVE-2014-0160</cve>
                <cvss3_base_score>9.8</cvss3_base_score>
                <cvss3_vector>CVSS:3.0/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N</cvss3_vector>
                <cvss_base_score>7.5</cvss_base_score>
                <exploit_available>true</exploit_available>
                <vuln_publication_date>2014/04/07</vuln_publication_date>
                <patch_publication_date>2014/04/07</patch_publication_date>
                <xref>OSVDB:105465</xref>
                <see_also>https://heartbleed.com</see_also>
              </ReportItem>
              <ReportItem port="0" svc_name="general" protocol="tcp" severity="0"
                          pluginID="19506" pluginName="Nessus Scan Information" pluginFamily="Settings">
                <description>Information about the scan.</description>
                <risk_factor>None</risk_factor>
              </ReportItem>
              <ReportItem port="80" svc_name="http" protocol="tcp" severity="2"
                          pluginID="99999" pluginName="" pluginFamily="Web Servers">
                <description>An item with no plugin name at all.</description>
                <risk_factor>Medium</risk_factor>
              </ReportItem>
            </ReportHost>
            <ReportHost name="10.0.0.2">
              <HostProperties>
                <tag name="host-ip">10.0.0.2</tag>
              </HostProperties>
              <ReportItem port="22" svc_name="ssh" protocol="tcp" severity="3"
                          pluginID="54321" pluginName="OpenSSH Weak Ciphers" pluginFamily="Misc.">
                <description>Weak ciphers are enabled.</description>
                <risk_factor>High</risk_factor>
              </ReportItem>
            </ReportHost>
          </Report>
        </NessusClientData_v2>
        """;

    /// <summary>
    /// SARIF from a code scanner: one error, one note, one suppressed result, and one with a
    /// GitHub security-severity score that lifts it into Critical.
    /// </summary>
    public const string Sarif = """
        {
          "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
          "version": "2.1.0",
          "runs": [
            {
              "tool": {
                "driver": {
                  "name": "CodeQL",
                  "semanticVersion": "2.15.0",
                  "rules": [
                    {
                      "id": "js/sql-injection",
                      "shortDescription": { "text": "Database query built from user input" },
                      "fullDescription": { "text": "Building a query from untrusted input allows injection." },
                      "help": { "text": "Use a parameterised query." },
                      "helpUri": "https://codeql.github.com/js/sql-injection",
                      "defaultConfiguration": { "level": "error" },
                      "properties": { "tags": ["security", "external/cwe/cwe-089"], "security-severity": "9.8" }
                    },
                    {
                      "id": "js/unused-variable",
                      "shortDescription": { "text": "Unused variable" },
                      "defaultConfiguration": { "level": "note" }
                    }
                  ]
                }
              },
              "results": [
                {
                  "ruleId": "js/sql-injection",
                  "level": "error",
                  "message": { "text": "This query depends on a user-provided value." },
                  "locations": [
                    {
                      "physicalLocation": {
                        "artifactLocation": { "uri": "src/db.js" },
                        "region": { "startLine": 42, "snippet": { "text": "db.query(`SELECT ${id}`)" } }
                      }
                    }
                  ]
                },
                {
                  "ruleId": "js/unused-variable",
                  "level": "note",
                  "message": { "text": "Variable x is never used." },
                  "locations": [
                    { "physicalLocation": { "artifactLocation": { "uri": "src/util.js" }, "region": { "startLine": 7 } } }
                  ]
                },
                {
                  "ruleId": "js/sql-injection",
                  "level": "error",
                  "message": { "text": "Suppressed in code." },
                  "suppressions": [ { "kind": "inSource" } ],
                  "locations": [
                    { "physicalLocation": { "artifactLocation": { "uri": "src/legacy.js" }, "region": { "startLine": 1 } } }
                  ]
                }
              ]
            }
          ]
        }
        """;

    /// <summary>ZAP: one alert on two URLs, one informational alert.</summary>
    public const string Zap = """
        {
          "@programName": "ZAP",
          "@version": "2.14.0",
          "@generated": "2026-08-01T10:00:00Z",
          "site": [
            {
              "@name": "https://app.example.com",
              "@host": "app.example.com",
              "@port": "443",
              "@ssl": "true",
              "alerts": [
                {
                  "pluginid": "10038",
                  "alertRef": "10038-1",
                  "alert": "Content Security Policy (CSP) Header Not Set",
                  "riskcode": "2",
                  "riskdesc": "Medium (High)",
                  "desc": "<p>No CSP header was set.</p>",
                  "solution": "<p>Set a Content-Security-Policy header.</p>",
                  "reference": "<p>https://owasp.org/csp</p>",
                  "cweid": "693",
                  "instances": [
                    { "uri": "https://app.example.com/", "method": "GET", "evidence": "" },
                    { "uri": "https://app.example.com/login", "method": "GET", "evidence": "" }
                  ]
                },
                {
                  "pluginid": "10015",
                  "alert": "Re-examine Cache-control Directives",
                  "riskcode": "0",
                  "desc": "<p>Informational.</p>",
                  "instances": [ { "uri": "https://app.example.com/static/app.js", "method": "GET" } ]
                }
              ]
            }
          ]
        }
        """;

    /// <summary>Trivy: a package CVE, a failed misconfiguration, a passed one, and a secret.</summary>
    public const string Trivy = """
        {
          "SchemaVersion": 2,
          "ArtifactName": "registry.example.com/app:1.4.2",
          "ArtifactType": "container_image",
          "Results": [
            {
              "Target": "registry.example.com/app:1.4.2 (alpine 3.18.4)",
              "Class": "os-pkgs",
              "Type": "alpine",
              "Vulnerabilities": [
                {
                  "VulnerabilityID": "CVE-2023-5678",
                  "PkgName": "openssl",
                  "InstalledVersion": "3.1.3-r0",
                  "FixedVersion": "3.1.4-r0",
                  "Title": "openssl: excessive time spent checking DH keys",
                  "Description": "Applications that use DH_check may experience long delays.",
                  "Severity": "HIGH",
                  "CweIDs": ["CWE-400"],
                  "CVSS": {
                    "redhat": { "V3Vector": "CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:N/I:N/A:L", "V3Score": 3.7 },
                    "nvd": { "V3Vector": "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:H", "V3Score": 7.5 }
                  },
                  "PrimaryURL": "https://avd.aquasec.com/nvd/cve-2023-5678",
                  "PublishedDate": "2023-10-24T00:00:00Z"
                }
              ]
            },
            {
              "Target": "Dockerfile",
              "Class": "config",
              "Type": "dockerfile",
              "Misconfigurations": [
                {
                  "ID": "DS002",
                  "AVDID": "AVD-DS-0002",
                  "Title": "Image user should not be 'root'",
                  "Description": "Running containers as root is dangerous.",
                  "Message": "Specify at least 1 USER command with a non-root user.",
                  "Resolution": "Add a USER command with a non-root user.",
                  "Severity": "HIGH",
                  "Status": "FAIL",
                  "PrimaryURL": "https://avd.aquasec.com/misconfig/ds002",
                  "CauseMetadata": { "StartLine": 1 }
                },
                {
                  "ID": "DS026",
                  "Title": "No HEALTHCHECK defined",
                  "Severity": "LOW",
                  "Status": "PASS"
                }
              ],
              "Secrets": [
                {
                  "RuleID": "aws-access-key-id",
                  "Category": "AWS",
                  "Severity": "CRITICAL",
                  "Title": "AWS Access Key ID",
                  "StartLine": 12,
                  "Match": "ENV AWS_ACCESS_KEY_ID=****************"
                }
              ]
            }
          ]
        }
        """;

    /// <summary>Semgrep native JSON: one finding, one nosemgrep-suppressed, one parse error.</summary>
    public const string Semgrep = """
        {
          "version": "1.45.0",
          "results": [
            {
              "check_id": "python.django.security.injection.sql.sql-injection-db-cursor-execute",
              "path": "app/views.py",
              "start": { "line": 88, "col": 9 },
              "end": { "line": 88, "col": 60 },
              "extra": {
                "message": "Detected SQL statement that is tainted by user input.",
                "severity": "WARNING",
                "fingerprint": "b1946ac92492d2347c6235b4d2611184",
                "lines": "cursor.execute(\"SELECT * FROM t WHERE id = %s\" % request.GET['id'])",
                "is_ignored": false,
                "metadata": {
                  "cwe": ["CWE-89: Improper Neutralization of Special Elements used in an SQL Command"],
                  "impact": "HIGH",
                  "references": ["https://owasp.org/sql-injection"],
                  "shortlink": "https://sg.run/abcd"
                }
              }
            },
            {
              "check_id": "python.lang.security.audit.eval-detected",
              "path": "app/legacy.py",
              "start": { "line": 3, "col": 1 },
              "extra": {
                "message": "eval() detected.",
                "severity": "ERROR",
                "is_ignored": true
              }
            }
          ],
          "errors": [
            { "message": "Syntax error while parsing", "path": "app/broken.py" }
          ]
        }
        """;

    /// <summary>OpenVAS/GVM: a high result with pipe-delimited NVT tags, and a Log result.</summary>
    public const string OpenVas = """
        <?xml version="1.0"?>
        <report>
          <scan_end>2026-08-01T12:00:00Z</scan_end>
          <results>
            <result id="r1">
              <name>SSL/TLS: Report Weak Cipher Suites</name>
              <host>10.0.0.5<hostname>db.example.com</hostname></host>
              <port>https (443/tcp)</port>
              <threat>High</threat>
              <severity>7.5</severity>
              <qod><value>98</value><type>remote_banner</type></qod>
              <description>Weak cipher suites accepted: TLS_RSA_WITH_3DES_EDE_CBC_SHA</description>
              <nvt oid="1.3.6.1.4.1.25623.1.0.103440">
                <name>SSL/TLS: Report Weak Cipher Suites</name>
                <family>SSL and TLS</family>
                <cvss_base>7.5</cvss_base>
                <severities score="7.5">
                  <severity type="cvss_base_v3">
                    <score>7.4</score>
                    <value>CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:H/I:N/A:N</value>
                  </severity>
                </severities>
                <refs>
                  <ref type="cve" id="CVE-2016-2183"/>
                  <ref type="url" id="https://sweet32.info"/>
                </refs>
                <tags>cvss_base_vector=AV:N/AC:L/Au:N/C:P/I:N/A:N|summary=Weak ciphers are accepted.|solution=Disable them.|insight=3DES is a 64-bit block cipher.</tags>
                <solution type="Mitigation">Reconfigure the service.</solution>
              </nvt>
            </result>
            <result id="r2">
              <name>Traceroute</name>
              <host>10.0.0.5</host>
              <port>general/tcp</port>
              <threat>Log</threat>
              <description>A traceroute was performed.</description>
              <nvt oid="1.3.6.1.4.1.25623.1.0.51662"><name>Traceroute</name><cvss_base>0.0</cvss_base></nvt>
            </result>
          </results>
        </report>
        """;

    /// <summary>Burp Professional XML: one high issue, one information issue.</summary>
    public const string BurpXml = """
        <?xml version="1.0"?>
        <issues burpVersion="2024.2.1">
          <issue>
            <serialNumber>1234567890</serialNumber>
            <type>1048832</type>
            <name>Cross-site scripting (reflected)</name>
            <host ip="203.0.113.10">https://shop.example.com</host>
            <path>/search</path>
            <location>/search [q parameter]</location>
            <severity>High</severity>
            <confidence>Certain</confidence>
            <issueBackground><p>Reflected XSS arises when input is echoed unsafely.</p></issueBackground>
            <remediationBackground><p>Validate input and encode output.</p></remediationBackground>
            <issueDetail><p>The value of the <b>q</b> parameter is copied into the response.</p></issueDetail>
            <vulnerabilityClassifications><li>CWE-79: Improper Neutralization of Input</li></vulnerabilityClassifications>
          </issue>
          <issue>
            <type>5245344</type>
            <name>Frameable response (potential Clickjacking)</name>
            <host ip="203.0.113.10">https://shop.example.com</host>
            <path>/</path>
            <severity>Information</severity>
            <confidence>Certain</confidence>
            <issueBackground><p>The page can be framed.</p></issueBackground>
          </issue>
        </issues>
        """;

    /// <summary>Snyk Open Source: one upgradable high, with a dependency path.</summary>
    public const string Snyk = """
        {
          "ok": false,
          "projectName": "shop-frontend",
          "displayTargetFile": "package-lock.json",
          "packageManager": "npm",
          "dependencyCount": 812,
          "vulnerabilities": [
            {
              "id": "SNYK-JS-LODASH-1040724",
              "title": "Prototype Pollution",
              "severity": "high",
              "packageName": "lodash",
              "version": "4.17.15",
              "fixedIn": ["4.17.19"],
              "from": ["shop-frontend@1.0.0", "webpack@4.44.1", "lodash@4.17.15"],
              "upgradePath": ["shop-frontend@1.0.0", "webpack@5.0.0"],
              "isUpgradable": true,
              "cvssScore": 7.4,
              "CVSSv3": "CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:H/I:H/A:H",
              "description": "lodash before 4.17.19 is vulnerable to prototype pollution.",
              "identifiers": { "CVE": ["CVE-2020-8203"], "CWE": ["CWE-1321"] },
              "references": [ { "title": "GitHub Commit", "url": "https://github.com/lodash/lodash/commit/abc" } ],
              "publicationTime": "2020-07-15T00:00:00Z"
            }
          ]
        }
        """;

    /// <summary>Grype: one match with a v3 CVSS and a GHSA-to-CVE relation.</summary>
    public const string Grype = """
        {
          "matches": [
            {
              "vulnerability": {
                "id": "GHSA-jjjj-kkkk-llll",
                "dataSource": "https://github.com/advisories/GHSA-jjjj-kkkk-llll",
                "namespace": "github:language:python",
                "severity": "Critical",
                "description": "Remote code execution in the template engine.",
                "urls": ["https://github.com/advisories/GHSA-jjjj-kkkk-llll"],
                "cvss": [
                  { "version": "2.0", "vector": "AV:N/AC:L/Au:N/C:P/I:P/A:P", "metrics": { "baseScore": 7.5 } },
                  { "version": "3.1", "vector": "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H", "metrics": { "baseScore": 9.8 } }
                ],
                "fix": { "versions": ["3.1.3"], "state": "fixed" }
              },
              "relatedVulnerabilities": [ { "id": "CVE-2024-22195" } ],
              "artifact": {
                "name": "jinja2",
                "version": "3.1.2",
                "type": "python",
                "purl": "pkg:pypi/jinja2@3.1.2",
                "locations": [ { "path": "/usr/lib/python3.11/site-packages/jinja2" } ]
              }
            }
          ],
          "source": { "type": "image", "target": { "userInput": "app:latest" } },
          "descriptor": { "name": "grype", "version": "0.74.0" }
        }
        """;

    /// <summary>Dependabot alerts: one open, one already fixed (which must not be imported).</summary>
    public const string Dependabot = """
        [
          {
            "number": 17,
            "state": "open",
            "dependency": {
              "package": { "ecosystem": "pip", "name": "django" },
              "manifest_path": "requirements.txt",
              "scope": "runtime"
            },
            "security_advisory": {
              "ghsa_id": "GHSA-mmmm-nnnn-oooo",
              "cve_id": "CVE-2024-27351",
              "summary": "Potential regular expression denial of service",
              "description": "django.utils.text.Truncator is vulnerable to a ReDoS.",
              "severity": "medium",
              "identifiers": [ { "type": "GHSA", "value": "GHSA-mmmm-nnnn-oooo" }, { "type": "CVE", "value": "CVE-2024-27351" } ],
              "references": [ { "url": "https://www.djangoproject.com/security" } ],
              "cvss": { "vector_string": "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:L", "score": 5.3 },
              "cwes": [ { "cwe_id": "CWE-1333", "name": "Inefficient Regular Expression Complexity" } ],
              "published_at": "2024-03-04T00:00:00Z"
            },
            "security_vulnerability": {
              "package": { "ecosystem": "pip", "name": "django" },
              "severity": "medium",
              "vulnerable_version_range": "< 4.2.11",
              "first_patched_version": { "identifier": "4.2.11" }
            },
            "html_url": "https://github.com/example/app/security/dependabot/17",
            "created_at": "2024-03-05T09:00:00Z",
            "updated_at": "2024-03-05T09:00:00Z"
          },
          {
            "number": 12,
            "state": "fixed",
            "dependency": { "package": { "ecosystem": "npm", "name": "minimist" }, "manifest_path": "package.json" },
            "security_advisory": {
              "ghsa_id": "GHSA-pppp-qqqq-rrrr",
              "summary": "Prototype pollution",
              "severity": "critical"
            },
            "created_at": "2023-01-01T00:00:00Z"
          }
        ]
        """;
}
