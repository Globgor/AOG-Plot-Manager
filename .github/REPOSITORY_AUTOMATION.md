# Repository intake and documentation automation

Issue forms collect reproducible bug reports, feature requests, and maintenance
tasks. Forms apply `bug`, `enhancement`, or `task`. Blank issues and existing
PR templates remain available. Priority, assignee, duplicate detection, and
organization issue types remain maintainer decisions.

Use synthetic examples. Keep credentials, personal and client data, private source
material, and copyrighted scans out of issues, attachments, and Git.

## PR labels

`Repository PR labels` adds labels from `.github/labeler.yml` on same-repository
PR updates. It preserves existing labels and does not execute or check out PR
code. Use its manual PR-number input for older PRs or external-fork contributions.
The manual workflow becomes available after it exists on the default branch.

| Paths | Label |
| --- | --- |
| Documentation, Markdown, and RST | `documentation` |
| `.github/**` | `github_actions` |
| Test directories and test helpers | `testing` |

The additive labels `task`, `github_actions`, and `testing` must exist before
activation. Existing issue labels and priorities are preserved. Existing
branch-based labelers, when present, keep their own purpose and configuration.

## Documentation links

`Repository documentation links` reports default-branch Markdown and RST links
weekly on Monday at **04:13 UTC**, or manually. It does not start a
runner on every documentation push. Requests to private addresses are excluded;
no authentication token is supplied to Lychee. Reports appear in the job summary,
without a retained report artifact. The job has a ten-minute timeout.

Reports are initially advisory. Existing missing targets, templates that need
rendering, authentication requirements, rate limits, and temporary site outages
need human assessment. A successful reporting job does not mean every link works.
This scanner does not build documentation or validate all RST structure.

## Cost and permissions

Issue forms need no runner. Labels and link reports use open-source Actions and
no AI API, paid bot, or Copilot entitlement. This repository is public. Standard GitHub-hosted runners are free under GitHub's public-repository policy. Larger runners and separate storage allowances have different rules.

No billing policy, deployment, publishing credential, or paid service is enabled
by these files. Existing build, release, and deployment workflows remain separate.
Only the PR label job gets `pull-requests: write`; the documentation job has
`contents: read`. Actions are pinned to immutable commits. Labels do not select
or suppress application checks.

References: [issue forms](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-issue-forms),
[Labeler](https://github.com/actions/labeler),
[Lychee](https://github.com/lycheeverse/lychee-action),
and [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions).
