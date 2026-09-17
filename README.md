# vNext Domain Discovery

A structured template package for vNext workflow components with domain-based architecture.

## 📦 Installation

Create a new project with your domain name:

```bash
npx @burgan-tech/vnext-template <domain-name>
```

### Example

```bash
npx @burgan-tech/vnext-template user-management
```

This will create a new directory with your domain name, copy all template files, replace `discovery` instances, and install dependencies automatically.

### Install specific version

```bash
npx @burgan-tech/vnext-template@<version> <domain-name>
```

Example:

```bash
npx @burgan-tech/vnext-template@1.0.0 user-management
```

### Alternative: Install as dependency

```bash
npm install @burgan-tech/vnext-template
npm run setup <domain-name>
```

Or install specific version:

```bash
npm install @burgan-tech/vnext-template@<version>
npm run setup <domain-name>
```

Or use environment variable:

```bash
DOMAIN_NAME=user-management npm install @burgan-tech/vnext-template@<version>
```

## 🚀 Usage

After installation, your project structure will be:

```
<domain-name>/
├── Extensions/
├── Functions/
├── Schemas/
├── Tasks/
├── Views/
├── Mappings/
└── Workflows/
```

### Access components in code

```javascript
const vnextTemplate = require('@burgan-tech/vnext-template');

// Get domain configuration
const config = vnextTemplate.getDomainConfig();

// Get paths configuration
const paths = vnextTemplate.getPathsConfig();

// Get all schemas
const schemas = vnextTemplate.getSchemas();

// Get all workflows
const workflows = vnextTemplate.getWorkflows();

// Get all tasks
const tasks = vnextTemplate.getTasks();

// Get all mappings
const mappings = vnextTemplate.getMappings();

// Get component path for a specific type
const schemasPath = vnextTemplate.getComponentPath('schemas');
```

### Available methods

| Method | Description |
|--------|-------------|
| `getDomainConfig()` | Get domain configuration from vnext.config.json |
| `getPathsConfig()` | Get paths configuration with defaults |
| `getSchemas()` | Get all schemas |
| `getWorkflows()` | Get all workflows |
| `getTasks()` | Get all tasks |
| `getViews()` | Get all views |
| `getFunctions()` | Get all functions |
| `getExtensions()` | Get all extensions |
| `getMappings()` | Get all mappings |
| `getDomainName()` | Get domain directory name |
| `getAvailableTypes()` | Get list of available component types |
| `getComponentPath(type)` | Get full path for a component type |

## ⚙️ Configuration

The `vnext.config.json` file allows you to customize paths and exports:

```json
{
  "domain": "my-domain",
  "paths": {
    "componentsRoot": "my-domain",
    "schemas": "Schemas",
    "workflows": "Workflows",
    "tasks": "Tasks",
    "views": "Views",
    "functions": "Functions",
    "extensions": "Extensions",
    "mappings": "Mappings"
  },
  "exports": {
    "schemas": ["schema1.json", "schema2.json"],
    "workflows": ["workflow1.json"],
    "tasks": [],
    "views": [],
    "functions": [],
    "extensions": [],
    "mappings": []
  }
}
```

### Path Configuration

You can customize component directory names:

```json
{
  "paths": {
    "componentsRoot": "src",
    "workflows": "Flows",
    "schemas": "Models"
  }
}
```

## ✅ Validation

Validate your project structure and schemas:

```bash
npm run validate
```

This will check:
- Package.json structure and content
- Main entry point functionality
- vnext.config.json validation
- Domain directory structure
- JSON file syntax validation
- Schema validation using @burgan-tech/vnext-schema
- Module functionality
- Semantic versioning compliance

### Validation Output

The validation provides detailed output with:
- ✅ Passed validations
- ❌ Failed validations with file paths and line numbers
- 📊 Summary statistics
- 📋 Failed files summary for easy navigation

## 🏗️ Build

Build your domain package for deployment or cross-domain usage:

```bash
# Runtime build (default) - Complete domain structure
npm run build

# Reference build - Only exported components
npm run build:reference

# Runtime build explicitly
npm run build:runtime
```

### Build Options

```bash
npm run build -- [options]

Options:
  -o, --output <dir>     Output directory (default: dist)
  -t, --type <type>      Build type: reference or runtime (default: runtime)
  --skip-validation      Skip validation during build
  -h, --help             Show help message
```

### Build Types

| Type | Description | Use Case |
|------|-------------|----------|
| `runtime` | Complete domain structure with all files | Engine deployment |
| `reference` | Only exported components from vnext.config.json | Cross-domain usage |

### Examples

```bash
# Build to custom directory
npm run build -- -o my-build

# Reference build to custom directory
npm run build -- -t reference -o packages/ref

# Skip validation for faster builds
npm run build -- --skip-validation
```

### Build Output Structure

**Runtime Build:**
```
dist/
├── <domain>/
│   ├── Extensions/
│   ├── Functions/
│   ├── Schemas/
│   ├── Tasks/
│   ├── Views/
│   ├── Mappings/
│   └── Workflows/
├── vnext.config.json
├── package.json
├── README.md
└── LICENSE
```

**Reference Build:**
```
dist/
├── <domain>/
│   ├── Extensions/     # Only exported files
│   ├── Functions/      # Only exported files
│   ├── Schemas/        # Only exported files
│   ├── Tasks/          # Only exported files
│   ├── Views/          # Only exported files
│   ├── Mappings/       # Only exported files
│   └── Workflows/      # Only exported files
├── vnext.config.json
├── package.json
├── README.md
└── LICENSE
```

## 🔎 Discovery Endpoints

The `discovery` domain exposes two domain-scoped (`scope: "D"`) read functions over the `domain`
lifecycle workflow. Both are served through the runtime's read-through **function cache**, so a
cache hit never touches the instance store.

| Endpoint | Function | Returns |
|---|---|---|
| `GET /api/v1/discovery/functions/domain-lookup?key={domainName}` | `domain-lookup` | One domain: `{ "data": { domainName, baseUrl, appId, healthUrl }, "eTag": "..." }`, or **404** when the domain is not registered |
| `GET /api/v1/discovery/functions/domain-list` | `domain-list` | Every active domain: `{ "items": [ { domainName, baseUrl, appId, healthUrl } ] }` — **200 with an empty array** when none are registered |

`domain-lookup` is what the runtime itself calls to resolve a single domain endpoint
(`ServiceDiscoveryOptions.DiscoveryEndpointTemplate`). `domain-list` is the enumeration
counterpart for callers that need the whole registry in one round trip.

### Cache and invalidation

| | `domain-lookup` | `domain-list` |
|---|---|---|
| Cache key | `keyExpression`: `"discovery:domain:" + key` | static `key`: `discovery:domains:active` |
| TTL | 86400s | 86400s |
| Evicted by | `invalidate-domain-cache` | `invalidate-domain-list-cache` |

Both eviction tasks are wired into the `domain` workflow's `start-domain` **and** `update`
transitions, so registering or updating any domain drops the stale entries immediately rather than
waiting out the TTL. They share `order: 1`, so the runtime executes them in parallel, and both carry
`errorBoundary` action `3` (Ignore) — a failed eviction must never fail a domain registration.

Key formats live in one place, `Mappings/src/DomainCacheKey.csx`: `DomainCacheKey.For(domainName)`
for the per-domain key and `DomainCacheKey.ListKey` for the list key. The read side of
`domain-lookup` mirrors that format in a Dynamic Expresso expression, which **cannot** call the
helper — if the two ever diverge, the cache is silently never invalidated. Change them in the same
commit.

> **Editing the `.csx` files:** every mapping is stored twice — as a file under `src/` and as
> base64 in the component JSON's `code` field. The runtime executes the **base64**, and `npm run
> validate` does not compare the two. Always re-encode the `code` field in the same commit as the
> `.csx` edit, and remember that `domain-workflow.json` embeds each workflow mapping **twice**
> (once per transition).

## 📜 Available Scripts

| Script | Description |
|--------|-------------|
| `npm run validate` | Validate project structure and schemas |
| `npm run build` | Build runtime package to dist/ |
| `npm run build:runtime` | Build runtime package explicitly |
| `npm run build:reference` | Build reference package with exports only |
| `npm run setup <name>` | Setup domain with given name |
| `npm run sync-schema` | Sync schema version from dependencies |
| `npm test` | Run tests |

## 📄 License

MIT
