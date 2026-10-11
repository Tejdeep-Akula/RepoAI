import { useCallback, useEffect, useState } from 'react'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080').replace(/\/$/, '')

type Snapshot = { id: string; commitSha: string; resolvedRef: string; fileCount: number; importedAtUtc: string }
type Job = { id: string; status: string; progressPercentage: number; failureCode: string | null; failureMessage: string | null }
type Storyboard = {
  snapshotId: string
  commitSha: string
  model: string
  generatedAtUtc: string
  storyboard: {
    title: string
    summary: string
    narrationScript: string
    scenes: { order: number; title: string; purpose: string; narration: string; visualInstructions: string; estimatedDurationSeconds: number; evidence: { path: string; startLine: number | null; endLine: number | null }[] }[]
  }
}

type Props = { projectId: string; authorization: string; onUnauthorized: () => void }

async function request<T>(url: string, authorization: string, init?: RequestInit): Promise<{ status: number; data: T | null }> {
  const response = await fetch(url, { ...init, headers: { Authorization: authorization, ...(init?.headers ?? {}) } })
  if (response.status === 401) throw new Error('unauthorized')
  if (response.status === 404) return { status: 404, data: null }
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`
    try { const problem = await response.json() as { detail?: string; title?: string }; detail = problem.detail ?? problem.title ?? detail } catch { /* no response body */ }
    throw new Error(detail)
  }
  return { status: response.status, data: response.status === 204 ? null : await response.json() as T }
}

function ProjectStoryboardPage({ projectId, authorization, onUnauthorized }: Props) {
  const [snapshots, setSnapshots] = useState<Snapshot[]>([])
  const [snapshotId, setSnapshotId] = useState('')
  const [storyboard, setStoryboard] = useState<Storyboard | null>(null)
  const [hasArchitecture, setHasArchitecture] = useState(false)
  const [loading, setLoading] = useState(true)
  const [generating, setGenerating] = useState(false)
  const [job, setJob] = useState<Job | null>(null)
  const [error, setError] = useState('')

  const loadStoryboard = useCallback(async (id: string) => {
    if (!id) return
    setLoading(true)
    setError('')
    try {
      const route = `${apiBaseUrl}/api/v1/projects/${projectId}/snapshots/${id}`
      const [saved, architecture] = await Promise.all([
        request<Storyboard>(`${route}/storyboard`, authorization),
        request<unknown>(`${route}/architecture`, authorization),
      ])
      setStoryboard(saved.data)
      setHasArchitecture(architecture.status === 200)
    } catch (cause) {
      if (cause instanceof Error && cause.message === 'unauthorized') onUnauthorized()
      else setError(cause instanceof Error ? cause.message : 'Could not load storyboard data.')
    } finally { setLoading(false) }
  }, [authorization, onUnauthorized, projectId])

  useEffect(() => {
    let active = true
    request<Snapshot[]>(`${apiBaseUrl}/api/v1/projects/${projectId}/snapshots`, authorization)
      .then(({ data }) => {
        if (!active) return
        const rows = data ?? []
        setSnapshots(rows)
        const initialSnapshotId = rows[0]?.id ?? ''
        setSnapshotId(initialSnapshotId)
        if (initialSnapshotId) void loadStoryboard(initialSnapshotId)
        else setLoading(false)
      })
      .catch((cause: unknown) => {
        if (cause instanceof Error && cause.message === 'unauthorized') onUnauthorized()
        else setError(cause instanceof Error ? cause.message : 'Could not load repository snapshots.')
        setLoading(false)
      })
    return () => { active = false }
  }, [authorization, loadStoryboard, onUnauthorized, projectId])

  useEffect(() => {
    if (!job || !['Queued', 'Running', 'RetryWaiting'].includes(job.status)) return
    const timer = window.setTimeout(async () => {
      try {
        const { data } = await request<Job>(`${apiBaseUrl}/api/v1/jobs/${job.id}`, authorization)
        if (!data) return
        setJob(data)
        if (data.status === 'Succeeded') {
          setGenerating(false)
          await loadStoryboard(snapshotId)
        } else if (['Failed', 'Cancelled'].includes(data.status)) {
          setGenerating(false)
          setError(data.failureMessage ?? `Storyboard generation ${data.status.toLowerCase()}.`)
        }
      } catch (cause) {
        setGenerating(false)
        if (cause instanceof Error && cause.message === 'unauthorized') onUnauthorized()
        else setError(cause instanceof Error ? cause.message : 'Could not check storyboard job status.')
      }
    }, 1800)
    return () => window.clearTimeout(timer)
  }, [authorization, job, loadStoryboard, onUnauthorized, snapshotId])

  async function generate() {
    setError('')
    setGenerating(true)
    try {
      const route = `${apiBaseUrl}/api/v1/projects/${projectId}/snapshots/${snapshotId}/storyboard`
      const { data } = await request<Job>(route, authorization, {
        method: 'POST',
        headers: { 'Idempotency-Key': crypto.randomUUID() },
      })
      setJob(data)
    } catch (cause) {
      setGenerating(false)
      if (cause instanceof Error && cause.message === 'unauthorized') onUnauthorized()
      else setError(cause instanceof Error ? cause.message : 'Could not start storyboard generation.')
    }
  }

  return <section className="storyboard-page">
    <div className="storyboard-intro"><div><span className="section-kicker">VIDEO PRE-PRODUCTION</span><h2>Storyboard</h2><p>Build a reviewable, evidence-grounded walkthrough from the saved architecture analysis. This step prepares the video plan; it does not render video yet.</p></div>
      <div className="storyboard-controls"><label htmlFor="storyboard-snapshot">Repository snapshot</label><select id="storyboard-snapshot" value={snapshotId} onChange={(event) => { setSnapshotId(event.target.value); setJob(null); void loadStoryboard(event.target.value) }}>
        {snapshots.length === 0 && <option value="">No imported snapshots</option>}
        {snapshots.map((item) => <option key={item.id} value={item.id}>{item.resolvedRef} · {item.commitSha.slice(0, 8)} · {new Date(item.importedAtUtc).toLocaleDateString()}</option>)}
      </select></div></div>
    {error && <div className="storyboard-error" role="alert">{error}</div>}
    {snapshots.length === 0 && <div className="library-empty-state"><strong>Import a repository snapshot first</strong><p>Storyboards are generated for a specific commit so the citations stay tied to reviewed source evidence.</p><a className="inline-action" href={`#/repositories/${projectId}/analysis`}>Open repository analysis</a></div>}
    {snapshots.length > 0 && loading && <p className="library-message">Loading storyboard…</p>}
    {snapshots.length > 0 && !loading && !storyboard && <div className="storyboard-empty"><span className="section-kicker">{hasArchitecture ? 'READY TO GENERATE' : 'ANALYSIS REQUIRED'}</span><h3>{hasArchitecture ? 'Turn this architecture into a walkthrough' : 'Analyze this snapshot first'}</h3><p>{hasArchitecture ? 'Ollama will draft a scene-by-scene narration and visual plan. Each scene must cite files or relationships present in the architecture report.' : 'A storyboard is based on the saved architecture report. Import and analyze this snapshot before generating one.'}</p>{hasArchitecture ? <button type="button" onClick={() => void generate()} disabled={generating}>{generating ? 'Generating…' : 'Generate storyboard'}</button> : <a className="inline-action" href={`#/repositories/${projectId}/analysis`}>Open repository analysis</a>}</div>}
    {generating && job && <div className="storyboard-job" role="status"><strong>{job.status === 'Queued' ? 'Waiting for the storyboard worker…' : 'Drafting storyboard…'}</strong><div className="storyboard-progress"><span style={{ width: `${job.progressPercentage}%` }} /></div><small>{job.progressPercentage}% · job {job.id}</small></div>}
    {storyboard && !loading && <>
      <div className="storyboard-heading"><div><span className="section-kicker">REVIEW DRAFT · {storyboard.model}</span><h3>{storyboard.storyboard.title}</h3><p>{storyboard.storyboard.summary}</p></div><button type="button" onClick={() => void generate()} disabled={generating}>{generating ? 'Generating…' : 'Regenerate'}</button></div>
      <div className="storyboard-scene-list">{storyboard.storyboard.scenes.map((scene) => <article className="storyboard-scene" key={scene.order}><div className="storyboard-scene-number">{String(scene.order).padStart(2, '0')}</div><div className="storyboard-scene-content"><div className="storyboard-scene-title"><div><span className="section-kicker">SCENE {scene.order} · {scene.estimatedDurationSeconds}s</span><h4>{scene.title}</h4></div></div><p className="storyboard-purpose">{scene.purpose}</p><p>{scene.narration}</p><div className="storyboard-visual"><span>VISUAL DIRECTION</span>{scene.visualInstructions}</div><div className="storyboard-evidence"><span>EVIDENCE</span>{scene.evidence.map((source) => <code key={`${source.path}:${source.startLine}:${source.endLine}`}>{source.path}{source.startLine ? `:${source.startLine}${source.endLine && source.endLine !== source.startLine ? `–${source.endLine}` : ''}` : ''}</code>)}</div></div></article>)}</div>
      <p className="storyboard-disclaimer">Review factual claims and scene evidence before rendering. Model-generated narration can still be mistaken; citations are restricted to evidence in the saved architecture report.</p>
    </>}
  </section>
}

export default ProjectStoryboardPage
