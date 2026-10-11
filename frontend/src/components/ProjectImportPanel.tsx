import { useCallback, useEffect, useState } from 'react'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080')
  .replace(/\/$/, '')
const filePageSize = 50

type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

type BackgroundJob = {
  id: string
  projectId: string
  kind: string
  status: string
  progressPercentage: number
  attemptCount: number
  maxAttempts: number
  failureCode: string | null
  failureMessage: string | null
}

type RepositorySnapshot = {
  id: string
  projectId: string
  repositoryUrl: string
  commitSha: string
  resolvedRef: string
  defaultBranch: string
  fileCount: number
  textFileCount: number
  binaryFileCount: number
  skippedFileCount: number
  importedAtUtc: string
}

type RepositoryFile = {
  relativePath: string
  detectedLanguage: string | null
  sizeBytes: number
  sha256: string
  isBinary: boolean
  contentAvailable: boolean
  skipReason: string | null
}

type RepositoryFilePage = {
  snapshotId: string
  page: number
  pageSize: number
  totalFiles: number
  files: RepositoryFile[]
}

type RepositoryArchitectureEvidence = {
  path: string
  reason: string
}

type RepositoryArchitectureLanguage = {
  name: string
  fileCount: number
}

type RepositoryArchitectureComponent = {
  name: string
  path: string
  fileCount: number
  languagesAndFormats: RepositoryArchitectureLanguage[]
  sourcePaths: string[]
}

type RepositoryArchitecture = {
  snapshotId: string
  commitSha: string
  analyzerVersion: string
  generatedAtUtc: string
  summary: string
  languagesAndFormats: RepositoryArchitectureLanguage[]
  components: RepositoryArchitectureComponent[]
  likelyEntryPoints: RepositoryArchitectureEvidence[]
  buildAndDependencyFiles: RepositoryArchitectureEvidence[]
  limitations: string[]
  findings: RepositoryArchitectureFinding[]
  graph?: RepositoryArchitectureGraph | null
}

type RepositoryArchitectureGraph = {
  nodes: RepositoryArchitectureGraphNode[]
  edges: RepositoryArchitectureGraphEdge[]
  isTruncated: boolean
  omittedEdgeCount: number
}

type RepositoryArchitectureGraphNode = {
  id: string
  label: string
  kind: string
  detail: string
  path: string | null
}

type RepositoryArchitectureGraphEdge = {
  id: string
  sourceNodeId: string
  targetNodeId: string
  relationshipType: string
  confidence: string
  evidence: { path: string; startLine: number | null; endLine: number | null }[]
}

type RepositoryArchitectureFinding = {
  category: string
  claim: string
  kind: 'Observation' | 'Inference'
  confidence: 'High' | 'Medium' | 'Low'
  method: string
  evidence: { path: string; startLine: number | null; endLine: number | null }[]
}

function ArchitectureGraph({ graph }: { graph: RepositoryArchitectureGraph | null | undefined }) {
  if (!graph || graph.edges.length === 0) {
    return (
      <div className="architecture-graph-empty">
        No evidence-backed dependency relationships were extracted for this snapshot.
      </div>
    )
  }

  const sourceIds = new Set(graph.edges.map((edge) => edge.sourceNodeId))
  const sources = graph.nodes.filter((node) => sourceIds.has(node.id))
  const targets = graph.nodes.filter((node) => !sourceIds.has(node.id))
  const visibleNodes = [...sources, ...targets]
  const positions = new Map<string, { x: number; y: number; side: 'left' | 'right' }>()
  sources.forEach((node, index) => positions.set(node.id, { x: 20, y: 28 + index * 74, side: 'left' }))
  targets.forEach((node, index) => positions.set(node.id, { x: 720, y: 28 + index * 74, side: 'right' }))
  const height = Math.max(150, Math.max(sources.length, targets.length) * 74 + 42)
  const nodeHeight = 52
  const nodeWidth = 260

  return (
    <div className="architecture-graph-wrap">
      <div className="architecture-graph-legend">
        <span><i className="graph-legend-component" /> Repository component</span>
        <span><i className="graph-legend-service" /> Compose service</span>
        <span><i className="graph-legend-package" /> Declared package</span>
        <span><i className="graph-legend-project" /> Project reference</span>
        <span><i className="graph-legend-service-edge" /> Compose startup dependency</span>
        <span><i className="graph-legend-http-edge" /> HTTP host reference</span>
        <span><i className="graph-legend-data-edge" /> Database / broker reference</span>
      </div>
      <div className="architecture-graph-scroll">
        <svg
          className="architecture-graph"
          viewBox={`0 0 1000 ${height}`}
          role="img"
          aria-label={`Architecture evidence graph with ${sources.length} source nodes, ${targets.length} target nodes, and ${graph.edges.length} relationships`}
        >
          <defs>
            <marker id="architecture-graph-arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto">
              <path d="M0,0 L8,4 L0,8 z" />
            </marker>
          </defs>
          {graph.edges.map((edge) => {
            const source = positions.get(edge.sourceNodeId)
            const target = positions.get(edge.targetNodeId)
            if (!source || !target) return null
            const sourceY = source.y + nodeHeight / 2
            const targetY = target.y + nodeHeight / 2
            const isInternal = target.side === 'left'
            const fromX = source.x + (source.side === 'left' ? nodeWidth : 0)
            const toX = target.x + (target.side === 'left' ? nodeWidth : 0)
            const middleX = isInternal ? 430 : 490
            const evidence = edge.evidence.map((item) => `${item.path}${item.startLine ? `:${item.startLine}` : ''}`).join(', ')
            const edgeClass = edge.relationshipType === 'compose_depends_on'
              ? 'compose-dependency'
              : edge.relationshipType === 'http_endpoint_reference'
                ? 'http-reference'
                : edge.relationshipType === 'database_provider_usage' || edge.relationshipType === 'message_broker_usage'
                  ? 'data-reference'
                  : isInternal ? 'project-reference' : 'package-dependency'
            return (
              <path
                key={edge.id}
                className={`architecture-graph-edge ${edgeClass}`}
                d={`M ${fromX} ${sourceY} C ${middleX} ${sourceY}, ${middleX} ${targetY}, ${toX} ${targetY}`}
                markerEnd="url(#architecture-graph-arrow)"
              >
                <title>{`${edge.relationshipType.replaceAll('_', ' ')} · ${edge.confidence} confidence · ${evidence}`}</title>
              </path>
            )
          })}
          {visibleNodes.map((node) => {
            const point = positions.get(node.id)!
            return (
              <g key={node.id} className={`architecture-graph-node ${node.kind}`} transform={`translate(${point.x}, ${point.y})`}>
                <title>{`${node.kind}: ${node.label} · ${node.detail}${node.path ? ` · ${node.path}` : ''}`}</title>
                <rect width={nodeWidth} height={nodeHeight} rx="8" />
                <text className="architecture-graph-label" x="12" y="21">
                  {node.label.length > 38 ? `${node.label.slice(0, 35)}…` : node.label}
                </text>
                <text className="architecture-graph-detail" x="12" y="39">{node.detail.length > 38 ? `${node.detail.slice(0, 35)}…` : node.detail}</text>
              </g>
            )
          })}
        </svg>
      </div>
      <p className="architecture-graph-caption">
        Arrows show manifest declarations or source-code references with evidence. Source matches are best-effort and do not prove runtime calls or complete code flow.
      </p>
      {graph.isTruncated && (
        <p className="architecture-graph-warning">Showing a bounded graph; {graph.omittedEdgeCount} edge(s) omitted.</p>
      )}
    </div>
  )
}

type ProjectImportPanelProps = {
  projectId: string
  authorization: string
  onUnauthorized: () => void
}

class ApiRequestError extends Error {
  constructor(message: string) {
    super(message)
  }
}

function explainProblem(problem: ProblemDetails, fallback: string) {
  const fieldErrors = problem.errors ? Object.values(problem.errors).flat() : []
  return fieldErrors[0] ?? problem.detail ?? problem.title ?? fallback
}

async function requestJson<T>(
  url: string,
  authorization: string,
  onUnauthorized: () => void,
  init?: RequestInit,
): Promise<T> {
  const response = await fetch(url, {
    ...init,
    headers: {
      Authorization: authorization,
      ...(init?.headers ?? {}),
    },
  })

  if (response.status === 401) {
    onUnauthorized()
    throw new ApiRequestError('Your RepoAI access token expired. Please sign in again.')
  }

  if (!response.ok) {
    let problem: ProblemDetails = {}
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      // Some network and proxy failures do not include a Problem Details body.
    }
    throw new ApiRequestError(explainProblem(problem, `Request failed (${response.status}).`))
  }

  return (await response.json()) as T
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function isActiveJob(status: string) {
  return ['Queued', 'Running', 'RetryWaiting', 'CancellationRequested'].includes(status)
}

function ProjectImportPanel({ projectId, authorization, onUnauthorized }: ProjectImportPanelProps) {
  const [job, setJob] = useState<BackgroundJob | null>(null)
  const [isQueueing, setIsQueueing] = useState(false)
  const [jobError, setJobError] = useState('')
  const [isSnapshotPanelOpen, setIsSnapshotPanelOpen] = useState(true)
  const [snapshots, setSnapshots] = useState<RepositorySnapshot[]>([])
  const [isLoadingSnapshots, setIsLoadingSnapshots] = useState(true)
  const [snapshotError, setSnapshotError] = useState('')
  const [selectedSnapshotId, setSelectedSnapshotId] = useState('')
  const [filePageNumber, setFilePageNumber] = useState(1)
  const [filePage, setFilePage] = useState<RepositoryFilePage | null>(null)
  const [fileError, setFileError] = useState('')
  const [snapshotRefreshKey, setSnapshotRefreshKey] = useState(0)
  const [architecture, setArchitecture] = useState<RepositoryArchitecture | null>(null)
  const [isArchitectureOpen, setIsArchitectureOpen] = useState(false)
  const [isLoadingArchitecture, setIsLoadingArchitecture] = useState(false)
  const [architectureError, setArchitectureError] = useState('')
  const [analysisJob, setAnalysisJob] = useState<BackgroundJob | null>(null)
  const [analysisSnapshotId, setAnalysisSnapshotId] = useState('')

  const loadSnapshots = useCallback(async (signal?: AbortSignal) => {
    try {
      const result = await requestJson<RepositorySnapshot[]>(
        `${apiBaseUrl}/api/v1/projects/${projectId}/snapshots`,
        authorization,
        onUnauthorized,
        { signal },
      )
      setSnapshots(result)
      setFileError('')
      setSelectedSnapshotId((currentId) =>
        currentId && result.some((snapshot) => snapshot.id === currentId)
          ? currentId
          : result[0]?.id ?? '',
      )
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setSnapshotError(
        error instanceof TypeError
          ? 'Could not reach RepoAI.Api. Check that the API is running.'
          : error instanceof Error
            ? error.message
            : 'Could not load repository snapshots.',
      )
    } finally {
      if (!signal?.aborted) setIsLoadingSnapshots(false)
    }
  }, [authorization, onUnauthorized, projectId])

  async function queueImport() {
    setIsQueueing(true)
    setJobError('')
    try {
      const createdJob = await requestJson<BackgroundJob>(
        `${apiBaseUrl}/api/v1/projects/${projectId}/imports`,
        authorization,
        onUnauthorized,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({}),
        },
      )
      setJob(createdJob)
    } catch (error) {
      setJobError(
        error instanceof TypeError
          ? 'Could not reach RepoAI.Api. Check that the API is running.'
          : error instanceof Error
            ? error.message
            : 'Could not queue this repository import.',
      )
    } finally {
      setIsQueueing(false)
    }
  }

  async function queueArchitectureAnalysis(snapshotId: string) {
    setIsLoadingArchitecture(true)
    setArchitectureError('')
    setArchitecture(null)
    setAnalysisJob(null)
    setAnalysisSnapshotId(snapshotId)
    try {
      const createdJob = await requestJson<BackgroundJob>(
        `${apiBaseUrl}/api/v1/projects/${projectId}/snapshots/${snapshotId}/analysis`,
        authorization,
        onUnauthorized,
        {
          method: 'POST',
          headers: { 'Idempotency-Key': crypto.randomUUID() },
        },
      )
      setAnalysisJob(createdJob)
    } catch (error) {
      setArchitectureError(
        error instanceof TypeError
          ? 'Could not reach RepoAI.Api. Check that the API is running.'
          : error instanceof Error
            ? error.message
            : 'Could not queue architecture analysis.',
      )
      setIsLoadingArchitecture(false)
    }
  }

  const activeJobId = job?.id
  const activeJobStatus = job?.status
  const activeAnalysisJobId = analysisJob?.id
  const activeAnalysisJobStatus = analysisJob?.status

  useEffect(() => {
    if (!activeJobId || !activeJobStatus || !isActiveJob(activeJobStatus)) return

    const controller = new AbortController()
    let timeoutId: number | undefined

    const pollJob = async () => {
      try {
        const updatedJob = await requestJson<BackgroundJob>(
          `${apiBaseUrl}/api/v1/jobs/${activeJobId}`,
          authorization,
          onUnauthorized,
          { signal: controller.signal },
        )
        if (controller.signal.aborted) return
        setJob(updatedJob)

        if (updatedJob.status === 'Succeeded') {
          setIsSnapshotPanelOpen(true)
          setIsLoadingSnapshots(true)
          setSnapshotError('')
          setSnapshotRefreshKey((key) => key + 1)
        } else if (updatedJob.status === 'Failed' || updatedJob.status === 'Cancelled') {
          setJobError('')
        } else {
          timeoutId = window.setTimeout(() => void pollJob(), 2500)
        }
      } catch (error) {
        if (controller.signal.aborted) return
        setJobError(
          error instanceof TypeError
            ? 'Could not reach RepoAI.Api while checking job progress.'
            : error instanceof Error
              ? error.message
              : 'Could not check job progress.',
        )
        timeoutId = window.setTimeout(() => void pollJob(), 5000)
      }
    }

    timeoutId = window.setTimeout(() => void pollJob(), 1500)
    return () => {
      controller.abort()
      if (timeoutId !== undefined) window.clearTimeout(timeoutId)
    }
  }, [activeJobId, activeJobStatus, authorization, onUnauthorized])

  useEffect(() => {
    if (!activeAnalysisJobId || !activeAnalysisJobStatus || !isActiveJob(activeAnalysisJobStatus)) return

    const controller = new AbortController()
    let timeoutId: number | undefined
    const pollAnalysisJob = async () => {
      try {
        const updatedJob = await requestJson<BackgroundJob>(
          `${apiBaseUrl}/api/v1/jobs/${activeAnalysisJobId}`,
          authorization,
          onUnauthorized,
          { signal: controller.signal },
        )
        if (controller.signal.aborted) return
        setAnalysisJob(updatedJob)
        if (updatedJob.status === 'Failed' || updatedJob.status === 'Cancelled') {
          setArchitectureError(
            updatedJob.failureMessage
              ?? updatedJob.failureCode
              ?? 'Architecture analysis did not complete.',
          )
          setIsLoadingArchitecture(false)
        } else if (isActiveJob(updatedJob.status)) {
          timeoutId = window.setTimeout(() => void pollAnalysisJob(), 2500)
        }
      } catch (error) {
        if (controller.signal.aborted) return
        setArchitectureError(
          error instanceof TypeError
            ? 'Could not reach RepoAI.Api while checking analysis progress.'
            : error instanceof Error
              ? error.message
              : 'Could not check architecture analysis progress.',
        )
        timeoutId = window.setTimeout(() => void pollAnalysisJob(), 5000)
      }
    }

    timeoutId = window.setTimeout(() => void pollAnalysisJob(), 1200)
    return () => {
      controller.abort()
      if (timeoutId !== undefined) window.clearTimeout(timeoutId)
    }
  }, [activeAnalysisJobId, activeAnalysisJobStatus, authorization, onUnauthorized])

  useEffect(() => {
    if (activeAnalysisJobStatus !== 'Succeeded' || !analysisSnapshotId) return

    const controller = new AbortController()
    const loadReport = async () => {
      try {
        const result = await requestJson<RepositoryArchitecture>(
          `${apiBaseUrl}/api/v1/projects/${projectId}/snapshots/${analysisSnapshotId}/architecture`,
          authorization,
          onUnauthorized,
          { signal: controller.signal },
        )
        if (controller.signal.aborted) return
        setArchitecture(result)
        setArchitectureError('')
      } catch (error) {
        if (controller.signal.aborted) return
        setArchitectureError(
          error instanceof TypeError
            ? 'Could not reach RepoAI.Api. Check that the API is running.'
            : error instanceof Error
              ? error.message
              : 'Could not load the saved architecture report.',
        )
      } finally {
        if (!controller.signal.aborted) setIsLoadingArchitecture(false)
      }
    }

    void loadReport()
    return () => controller.abort()
  }, [activeAnalysisJobStatus, analysisSnapshotId, authorization, onUnauthorized, projectId])

  useEffect(() => {
    if (!isSnapshotPanelOpen) return
    const controller = new AbortController()
    const timeoutId = window.setTimeout(() => void loadSnapshots(controller.signal), 0)
    return () => {
      controller.abort()
      window.clearTimeout(timeoutId)
    }
  }, [isSnapshotPanelOpen, loadSnapshots, snapshotRefreshKey])

  useEffect(() => {
    if (!isSnapshotPanelOpen || !selectedSnapshotId) {
      return
    }

    const controller = new AbortController()
    requestJson<RepositoryFilePage>(
      `${apiBaseUrl}/api/v1/projects/${projectId}/snapshots/${selectedSnapshotId}/files?page=${filePageNumber}&pageSize=${filePageSize}`,
      authorization,
      onUnauthorized,
      { signal: controller.signal },
    )
      .then(setFilePage)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFileError(
          error instanceof TypeError
            ? 'Could not reach RepoAI.Api. Check that the API is running.'
            : error instanceof Error
              ? error.message
              : 'Could not load snapshot files.',
        )
      })

    return () => controller.abort()
  }, [authorization, filePageNumber, isSnapshotPanelOpen, onUnauthorized, projectId, selectedSnapshotId])

  const isLoadingFiles = Boolean(selectedSnapshotId)
    && !fileError
    && (filePage?.snapshotId !== selectedSnapshotId || filePage.page !== filePageNumber)
  const hasMoreFiles = filePage !== null && filePage.page * filePage.pageSize < filePage.totalFiles
  const activeJob = job !== null && isActiveJob(job.status)

  return (
    <div className="repository-import-panel">
      <div className="repository-panel-actions">
        <button
          className="import-button"
          type="button"
          onClick={() => void queueImport()}
          disabled={isQueueing || activeJob || isLoadingSnapshots}
        >
          {isQueueing ? 'Queueing…' : activeJob ? 'Import in progress' : isLoadingSnapshots ? 'Checking snapshots…' : snapshots.length > 0 ? 'Import newer snapshot' : 'Import repository'}
        </button>
        <button
          className="snapshots-toggle"
          type="button"
          onClick={() => {
            if (isSnapshotPanelOpen) {
              setIsSnapshotPanelOpen(false)
            } else {
              setIsLoadingSnapshots(true)
              setSnapshotError('')
              setIsSnapshotPanelOpen(true)
            }
          }}
          aria-expanded={isSnapshotPanelOpen}
        >
          {isSnapshotPanelOpen ? 'Hide snapshots' : `Browse snapshots${snapshots.length ? ` · ${snapshots.length}` : ''}`}
        </button>
      </div>

      {job && (
        <section className="import-job-status" aria-live="polite" aria-label="Repository import status">
          <div className="import-job-heading">
            <strong>{job.status === 'Succeeded' ? 'Import complete' : job.status === 'Failed' ? 'Import failed' : 'Repository import'}</strong>
            <span className={`job-status-badge ${job.status.toLowerCase()}`}>{job.status}</span>
          </div>
          {activeJob && (
            <>
              <div className="job-progress-track">
                <div className="job-progress-value" style={{ width: `${job.progressPercentage}%` }} />
              </div>
              <div className="job-progress-copy">
                <span>{job.progressPercentage}%</span>
                <span>Attempt {job.attemptCount} of {job.maxAttempts}</span>
              </div>
              <p className="job-status-hint">
                {job.status === 'Queued'
                  ? 'Waiting for a Worker to pick up the import.'
                  : job.status === 'RetryWaiting'
                    ? 'A retry is scheduled.'
                    : 'The Worker is importing and indexing repository files.'}
              </p>
            </>
          )}
          {job.status === 'Failed' && (
            <div className="job-failure" role="alert">
              <strong>{job.failureCode ?? 'Import failed'}</strong>
              <span>{job.failureMessage ?? 'Check the Worker logs for more details.'}</span>
            </div>
          )}
          {job.status === 'Succeeded' && (
            <p className="job-success-copy">The repository snapshot is ready to browse.</p>
          )}
          {job.status === 'Cancelled' && <p className="job-status-hint">This import was cancelled.</p>}
        </section>
      )}
      {jobError && <p className="repository-panel-error" role="alert">{jobError}</p>}

      {isSnapshotPanelOpen && (
        <section className="snapshot-browser" aria-label="Repository snapshots">
          <div className="snapshot-browser-heading">
            <div>
              <span className="section-kicker">IMPORTED VERSIONS</span>
              <h3>Repository snapshots</h3>
            </div>
            <button
              className="snapshot-refresh"
              type="button"
              onClick={() => {
                setIsLoadingSnapshots(true)
                setSnapshotError('')
                setSnapshotRefreshKey((key) => key + 1)
              }}
              disabled={isLoadingSnapshots}
            >
              {isLoadingSnapshots ? 'Refreshing…' : 'Refresh'}
            </button>
          </div>

          {isLoadingSnapshots && snapshots.length === 0 && <p className="repository-panel-message" role="status">Loading snapshots…</p>}
          {snapshotError && <p className="repository-panel-error" role="alert">{snapshotError}</p>}
          {!isLoadingSnapshots && !snapshotError && snapshots.length === 0 && (
            <p className="repository-panel-message">No snapshots yet. Import this repository to create the first one.</p>
          )}

          {snapshots.length > 0 && (
            <>
              <label className="snapshot-select-label" htmlFor={`snapshot-${projectId}`}>Choose a snapshot</label>
              <select
                id={`snapshot-${projectId}`}
                className="snapshot-select"
                value={selectedSnapshotId}
                onChange={(event) => {
                  setFileError('')
                  const nextSnapshotId = event.target.value
                  setSelectedSnapshotId(nextSnapshotId)
                  setFilePageNumber(1)
                  if (isArchitectureOpen) void queueArchitectureAnalysis(nextSnapshotId)
                }}
              >
                {snapshots.map((snapshot) => (
                  <option key={snapshot.id} value={snapshot.id}>
                    {snapshot.resolvedRef} · {new Date(snapshot.importedAtUtc).toLocaleString()}
                  </option>
                ))}
              </select>

              {snapshots.filter((snapshot) => snapshot.id === selectedSnapshotId).map((snapshot) => (
                <div className="snapshot-summary" key={snapshot.id}>
                  <div className="snapshot-summary-counts">
                    <span><strong>{snapshot.fileCount}</strong> files</span>
                    <span><strong>{snapshot.textFileCount}</strong> text</span>
                    <span><strong>{snapshot.binaryFileCount}</strong> binary</span>
                    {snapshot.skippedFileCount > 0 && <span><strong>{snapshot.skippedFileCount}</strong> skipped</span>}
                  </div>
                  <p>Commit <code>{snapshot.commitSha.slice(0, 12)}</code> · default branch <code>{snapshot.defaultBranch}</code></p>
                </div>
              ))}

              <button
                className="architecture-toggle"
                type="button"
                aria-expanded={isArchitectureOpen}
                onClick={() => {
                  if (isArchitectureOpen) {
                    setIsArchitectureOpen(false)
                    return
                  }
                  setIsArchitectureOpen(true)
                  if (architecture?.snapshotId !== selectedSnapshotId) {
                    void queueArchitectureAnalysis(selectedSnapshotId)
                  }
                }}
                disabled={!selectedSnapshotId || isLoadingArchitecture}
              >
                {isLoadingArchitecture
                  ? 'Analyzing repository…'
                  : isArchitectureOpen
                    ? 'Hide architecture overview'
                    : 'Show architecture overview'}
              </button>

              {isArchitectureOpen && (
                <section className="architecture-overview" aria-label="Architecture overview">
                  {analysisJob && analysisSnapshotId === selectedSnapshotId && (
                    <div className="analysis-job-status" aria-live="polite">
                      <div className="import-job-heading">
                        <strong>Architecture analysis</strong>
                        <span className={`job-status-badge ${analysisJob.status.toLowerCase()}`}>{analysisJob.status}</span>
                      </div>
                      {isActiveJob(analysisJob.status) && (
                        <>
                          <div className="job-progress-track">
                            <div className="job-progress-value" style={{ width: `${analysisJob.progressPercentage}%` }} />
                          </div>
                          <div className="job-progress-copy">
                            <span>{analysisJob.progressPercentage}%</span>
                            <span>Attempt {analysisJob.attemptCount} of {analysisJob.maxAttempts}</span>
                          </div>
                        </>
                      )}
                    </div>
                  )}
                  {isLoadingArchitecture && !analysisJob && <p className="repository-panel-message" role="status">Queueing architecture analysis…</p>}
                  {architectureError && <p className="repository-panel-error" role="alert">{architectureError}</p>}
                  {architecture?.snapshotId === selectedSnapshotId && !isLoadingArchitecture && (
                    <>
                      <p className="architecture-summary">{architecture.summary}</p>
                      <p className="architecture-commit">Analyzed commit <code>{architecture.commitSha.slice(0, 12)}</code></p>
                      <p className="architecture-commit">Analyzer {architecture.analyzerVersion} · generated {new Date(architecture.generatedAtUtc).toLocaleString()}</p>

                      {architecture.languagesAndFormats.length > 0 && (
                        <div className="architecture-section">
                          <h4>Languages and formats</h4>
                          <div className="architecture-tags">
                            {architecture.languagesAndFormats.map((item) => (
                              <span key={item.name}>{item.name} <strong>{item.fileCount}</strong></span>
                            ))}
                          </div>
                        </div>
                      )}

                      <div className="architecture-section">
                        <h4>Evidence-backed dependency graph <span>{architecture.graph?.edges.length ?? 0} relationships</span></h4>
                        <ArchitectureGraph graph={architecture.graph} />
                      </div>

                      <ArchitectureSourceRelationships findings={architecture.findings} />

                      <div className="architecture-section">
                        <h4>Components <span>{architecture.components.length}</span></h4>
                        <div className="architecture-components">
                          {architecture.components.map((component) => (
                            <article className="architecture-component" key={component.path}>
                              <div className="architecture-component-heading">
                                <strong>{component.name}</strong>
                                <span>{component.fileCount} files</span>
                              </div>
                              <code className="architecture-component-path">{component.path}</code>
                              {component.languagesAndFormats.length > 0 && (
                                <p>{component.languagesAndFormats.map((language) => language.name).join(' · ')}</p>
                              )}
                              <div className="architecture-source-paths">
                                {component.sourcePaths.map((path) => <code key={path}>{path}</code>)}
                              </div>
                            </article>
                          ))}
                        </div>
                      </div>

                      <ArchitectureEvidence title="Likely entry points" items={architecture.likelyEntryPoints} />
                      <ArchitectureEvidence title="Build and dependency files" items={architecture.buildAndDependencyFiles} />

                      <details className="architecture-findings">
                        <summary>Findings and evidence ({architecture.findings.length})</summary>
                        <ul>
                          {architecture.findings.map((finding, index) => (
                            <li key={`${finding.category}-${index}`}>
                              <div className="architecture-finding-heading">
                                <strong>{finding.claim}</strong>
                                <span className={`finding-kind ${finding.kind.toLowerCase()}`}>{finding.kind}</span>
                                <span className={`finding-confidence ${finding.confidence.toLowerCase()}`}>{finding.confidence} confidence</span>
                              </div>
                              <span className="architecture-finding-method">Method: {finding.method.replaceAll('_', ' ')}</span>
                              <div className="architecture-finding-evidence">
                                {finding.evidence.map((reference) => (
                                  <code key={`${reference.path}-${reference.startLine ?? ''}`}>
                                    {reference.path}{reference.startLine ? `:${reference.startLine}${reference.endLine && reference.endLine !== reference.startLine ? `-${reference.endLine}` : ''}` : ''}
                                  </code>
                                ))}
                              </div>
                            </li>
                          ))}
                        </ul>
                      </details>

                      <div className="architecture-section architecture-limitations">
                        <h4>What this overview does not infer yet</h4>
                        <ul>
                          {architecture.limitations.map((limitation) => <li key={limitation}>{limitation}</li>)}
                        </ul>
                      </div>
                    </>
                  )}
                </section>
              )}

              {fileError && <p className="repository-panel-error" role="alert">{fileError}</p>}
              {isLoadingFiles && <p className="repository-panel-message" role="status">Loading file inventory…</p>}
              {filePage && !isLoadingFiles && (
                <>
                  <div className="file-inventory-heading">
                    <h4>File inventory</h4>
                    <span>{filePage.totalFiles.toLocaleString()} total</span>
                  </div>
                  {filePage.files.length === 0 ? (
                    <p className="repository-panel-message">This snapshot has no files on this page.</p>
                  ) : (
                    <div className="file-table-scroll">
                      <table className="file-table">
                        <thead>
                          <tr><th scope="col">Path</th><th scope="col">Language</th><th scope="col">Size</th><th scope="col">Content</th></tr>
                        </thead>
                        <tbody>
                          {filePage.files.map((file) => (
                            <tr key={file.relativePath}>
                              <td><code title={file.relativePath}>{file.relativePath}</code></td>
                              <td>{file.detectedLanguage ?? 'Unknown'}</td>
                              <td>{formatBytes(file.sizeBytes)}</td>
                              <td>
                                <span className={file.contentAvailable ? 'file-content-state available' : 'file-content-state'}>
                                  {file.skipReason ?? (file.contentAvailable ? 'Stored' : file.isBinary ? 'Binary metadata' : 'Metadata only')}
                                </span>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                  <div className="file-pagination">
                    <button type="button" onClick={() => setFilePageNumber((page) => Math.max(1, page - 1))} disabled={filePageNumber <= 1 || isLoadingFiles}>Previous</button>
                    <span>Page {filePage.page} of {Math.max(1, Math.ceil(filePage.totalFiles / filePage.pageSize))}</span>
                    <button type="button" onClick={() => setFilePageNumber((page) => page + 1)} disabled={!hasMoreFiles || isLoadingFiles}>Next</button>
                  </div>
                </>
              )}
            </>
          )}
        </section>
      )}
    </div>
  )
}

function ArchitectureEvidence({ title, items }: { title: string; items: RepositoryArchitectureEvidence[] }) {
  if (items.length === 0) return null
  return (
    <div className="architecture-section">
      <h4>{title}</h4>
      <ul className="architecture-evidence-list">
        {items.map((item) => (
          <li key={item.path}>
            <code>{item.path}</code>
            <span>{item.reason}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

const sourceRelationshipCategories = new Set([
  'http_endpoint_reference',
  'database_provider_usage',
  'message_broker_usage',
])

function ArchitectureSourceRelationships({ findings }: { findings: RepositoryArchitectureFinding[] }) {
  const sourceFindings = findings.filter((finding) => sourceRelationshipCategories.has(finding.category))

  return (
    <div className="architecture-section">
      <h4>Source-code relationships <span>{sourceFindings.length} detected</span></h4>
      <p className="architecture-source-flow-note">
        Best-effort references found while parsing source files. These show evidence in code, not proof that a call happens at runtime.
      </p>
      {sourceFindings.length === 0 ? (
        <div className="architecture-graph-empty">No HTTP host, database provider, or message-broker references were detected in the analyzed source files.</div>
      ) : (
        <ul className="architecture-source-flow-list">
          {sourceFindings.map((finding, index) => (
            <li key={`${finding.category}-${finding.evidence[0]?.path ?? index}-${finding.evidence[0]?.startLine ?? ''}`}>
              <div className="architecture-finding-heading">
                <strong>{finding.claim}</strong>
                <span className={`finding-confidence ${finding.confidence.toLowerCase()}`}>{finding.confidence} confidence</span>
                <span className="architecture-source-flow-type">{finding.category.replaceAll('_', ' ')}</span>
              </div>
              <span className="architecture-finding-method">Evidence-based source parser · {finding.kind.toLowerCase()}</span>
              <div className="architecture-finding-evidence">
                {finding.evidence.map((reference) => (
                  <code key={`${reference.path}-${reference.startLine ?? ''}`}>
                    {reference.path}{reference.startLine ? `:${reference.startLine}${reference.endLine && reference.endLine !== reference.startLine ? `-${reference.endLine}` : ''}` : ''}
                  </code>
                ))}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export default ProjectImportPanel
