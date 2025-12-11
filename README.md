# CatPilot

CatPilot is a lightweight AI coding agent designed to batch-process instruction prompts stored in a directory.

## Disclaimer

This tool was originally created for my personal workflows, but the design is general enough that others may find it
useful.

That said, CatPilot has **not** been developed or tested as a fully general-purpose coding agent. Its usefulness may
vary depending on your environment and project structure.

## Usage

### Prepare prompts and reference files

* Create a system prompt at `prompts/system/coding.md`.
  You can use `coding.example.md` as a template.

* Define tasks and subtasks under `prompts/tasks` using numbered directories and Markdown files.
  For example:

  ```
  prompts/tasks/01-migrate-from-webpack-to-vite
  ├── 01-delete-obsolete-files.md
  ├── 02-update-package-json.md
  ├── 03-copy-necessary-files.md
  ├── 04-update-env-and-source.md
  ├── 05-migrate-bz-components.md
  ├── 06-verify-migration-build.md
  └── 07-finalize-and-commit.md

  prompts/tasks/02-update-misc-dependencies
  ├── 01-update-package-json-deps.md
  ├── 02-update-vertx-eventbus-imports.md
  ├── 03-update-antd-modal.md
  ├── 04-update-antd-styled-modal.md
  ├── 05-update-antd-styled-popover.md
  ├── 06-update-antd-css.md
  ├── 07-update-immer-imports.md
  ├── 08-update-uuid-imports.md
  ├── 09-verify-build.md
  └── 10-finalize-and-commit.md
  ```

* Add any additional reference files to the `references` directory as needed.

### Example task definition

````markdown
# Verify Build After React Router Updates

## Objective

Install dependencies and ensure the project builds successfully after React and DnD-related updates.

## Steps

1. Install dependencies:
   ```shell
   pnpm install
````

2. Build the project (dev build):

   ```shell
   pnpm build:dev
   ```
3. Fix any trivial or obvious build errors.
4. If you encounter complex or unclear errors, pause and request guidance.

## Success Condition

* `pnpm install` runs without errors.
* `pnpm build:dev` completes successfully, or any non-trivial issues are explicitly paused for review.

````

### Running CatPilot

To process all prompts in a project directory:

```shell
CatPilot [project path]
````

CatPilot will read and execute all task and subtask instruction files in order.

### Resuming from a specific task

You can resume execution from a specific task and subtask:

```shell
CatPilot [project path] [task index]:[subtask index]
```

Example:

```shell
CatPilot ./my-project 12:3
```

This resumes at **task 12**, **subtask 3**.

## License

This project is licensed under the MIT License.
See [LICENSE](LICENSE) for details.
