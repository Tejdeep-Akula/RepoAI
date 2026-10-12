import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import ProjectImportPanel from './components/ProjectImportPanel'
import ProjectChatPanel from './components/ProjectChatPanel'
import ConversationLibraryPage from './components/ConversationLibraryPage'
import ProjectStoryboardPage from './components/ProjectStoryboardPage'
import './App.css'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080')
  .replace(/\/$/, '')

type ApiStatus = {
  application: string
  status: string
}

type RepositoryValidation = {
  repositoryUrl: string
  host: string
  reachabilityChecked: boolean
}

type Project = {
  id: string
  name: string
  repositoryUrl: string
  createdAtUtc: string
}

type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

type GoogleCredentialResponse = {
  credential: string
}

type GoogleIdentityApi = {
  initialize: (options: {
    client_id: string
    callback: (response: GoogleCredentialResponse) => void
    auto_select?: boolean
  }) => void
  renderButton: (
    element: HTMLElement,
    options: {
      theme: 'outline'
      size: 'large'
      shape: 'pill'
      text: 'signin_with'
      width: number
    },
  ) => void
  disableAutoSelect: () => void
}

declare global {
  interface Window {
    google?: {
      accounts?: {
        id?: GoogleIdentityApi
      }
    }
  }
}

type RepoAiSignInResponse = {
  userId: string
  displayName: string
  email: string | null
  accessToken: string
  tokenType: string
  expiresAtUtc: string
}

let googleIdentityScriptPromise: Promise<void> | undefined

function loadGoogleIdentityScript() {
  if (window.google?.accounts?.id) return Promise.resolve()
  if (googleIdentityScriptPromise) return googleIdentityScriptPromise

  googleIdentityScriptPromise = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = 'https://accounts.google.com/gsi/client'
    script.async = true
    script.defer = true
    script.onload = () => resolve()
    script.onerror = () => {
      googleIdentityScriptPromise = undefined
      reject(new Error('Could not load Google Sign-In. Check your internet connection and try again.'))
    }
    document.head.append(script)
  })

  return googleIdentityScriptPromise
}

function Icon({ name }: { name: 'mark' | 'grid' | 'folder' | 'film' | 'chat' | 'arrow' | 'spark' | 'github' }) {
  const paths = {
    mark: <><path d="M5 7.5 12 4l7 3.5v9L12 20l-7-3.5z" /><path d="m5.5 7.8 6.5 3.4 6.5-3.4M12 11.2V20M8.5 5.8 15 9.2" /></>,
    grid: <><rect x="4" y="4" width="6" height="6" rx="1.5" /><rect x="14" y="4" width="6" height="6" rx="1.5" /><rect x="4" y="14" width="6" height="6" rx="1.5" /><rect x="14" y="14" width="6" height="6" rx="1.5" /></>,
    folder: <><path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" /><path d="M3.8 10h16.4" /></>,
    film: <><rect x="4" y="5" width="16" height="14" rx="2" /><path d="m10 9 5 3-5 3zM8 5v2m8-2v2M8 17v2m8-2v2" /></>,
    chat: <><path d="M20 11.5a7.5 7.5 0 0 1-8 7.5 8.8 8.8 0 0 1-3.5-.7L4 20l1.2-3.2A7.2 7.2 0 0 1 4 12c0-4.2 3.6-7.5 8-7.5s8 2.8 8 7z" /><path d="M8 12h.01M12 12h.01M16 12h.01" /></>,
    arrow: <><path d="M5 12h14M13 6l6 6-6 6" /></>,
    spark: <><path d="m12 3 1.7 5.3L19 10l-5.3 1.7L12 17l-1.7-5.3L5 10l5.3-1.7z" /><path d="m19 16 .7 2.3L22 19l-2.3.7L19 22l-.7-2.3L16 19l2.3-.7z" /></>,
    github: <><path d="M9 19c-4.3 1.3-4.3-2.5-6-3m12 6v-3.9a3.4 3.4 0 0 0-.9-2.6c3-.3 6.1-1.5 6.1-6.8a5.3 5.3 0 0 0-1.4-3.7 4.9 4.9 0 0 0-.1-3.7s-1.2-.4-4 1.4a13.7 13.7 0 0 0-7.3 0C4.6.9 3.4 1.3 3.4 1.3a4.9 4.9 0 0 0-.1 3.7A5.3 5.3 0 0 0 2 8.7c0 5.3 3.1 6.5 6.1 6.8a3.4 3.4 0 0 0-.9 2.6V22" /></>,
  }

  return <svg className="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

function explainProblem(problem: ProblemDetails, fallback: string) {
  const fieldErrors = problem.errors ? Object.values(problem.errors).flat() : []
  return fieldErrors[0] ?? problem.detail ?? problem.title ?? fallback
}

function App() {
  const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID?.trim()
  const [routePath, setRoutePath] = useState(() => window.location.hash.startsWith('#/')
    ? window.location.hash.slice(1)
    : '/overview')
  const [apiStatus, setApiStatus] = useState<'checking' | 'online' | 'offline'>('checking')
  const [apiMessage, setApiMessage] = useState('Checking API…')
  const [repositoryUrl, setRepositoryUrl] = useState('')
  const [validation, setValidation] = useState<RepositoryValidation | null>(null)
  const [validationError, setValidationError] = useState('')
  const [isValidating, setIsValidating] = useState(false)
  const [signIn, setSignIn] = useState<RepoAiSignInResponse | null>(null)
  const [signInError, setSignInError] = useState('')
  const [isSigningIn, setIsSigningIn] = useState(false)
  const [projects, setProjects] = useState<Project[]>([])
  const [projectLoadStatus, setProjectLoadStatus] = useState<'idle' | 'loading' | 'loaded' | 'error'>('idle')
  const [projectLoadError, setProjectLoadError] = useState('')
  const [projectReloadKey, setProjectReloadKey] = useState(0)
  const [isProjectFormOpen, setIsProjectFormOpen] = useState(false)
  const [projectName, setProjectName] = useState('')
  const [projectRepositoryUrl, setProjectRepositoryUrl] = useState('')
  const [isCreatingProject, setIsCreatingProject] = useState(false)
  const [createProjectError, setCreateProjectError] = useState('')
  const [googleScriptError, setGoogleScriptError] = useState('')
  const [googleButtonContainer, setGoogleButtonContainer] = useState<HTMLDivElement | null>(null)
  const googleIdentityInitialized = useRef(false)

  useEffect(() => {
    const syncRoute = () => setRoutePath(window.location.hash.startsWith('#/')
      ? window.location.hash.slice(1)
      : '/overview')
    window.addEventListener('hashchange', syncRoute)
    window.addEventListener('popstate', syncRoute)
    return () => {
      window.removeEventListener('hashchange', syncRoute)
      window.removeEventListener('popstate', syncRoute)
    }
  }, [])

  function navigate(path: string) {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`
    if (window.location.hash === `#${normalizedPath}`) {
      setRoutePath(normalizedPath)
    } else {
      window.history.pushState(null, '', `#${normalizedPath}`)
      setRoutePath(normalizedPath)
    }
  }

  const routeSegments = routePath.split('/').filter(Boolean)
  const isRepositoryRoute = routeSegments[0] === 'repositories' && routeSegments.length >= 2
  const selectedProject = isRepositoryRoute
    ? projects.find((project) => project.id === routeSegments[1])
    : undefined
  const repositoryPage = routeSegments[2] ?? 'overview'
  const isConversationRoute = isRepositoryRoute && repositoryPage === 'conversations'
  const isChatRoute = isConversationRoute && routeSegments.length >= 4
  const conversationId = isChatRoute ? routeSegments[3] : undefined
  const routeIsOverview = routeSegments.length === 0 || routeSegments[0] === 'overview'
  const routeIsRepositories = routeSegments[0] === 'repositories' && !isRepositoryRoute
  const routeIsConversations = routeSegments[0] === 'conversations'
  const routeIsStoryboards = routeSegments[0] === 'storyboards' || (isRepositoryRoute && repositoryPage === 'storyboards')
  const authorization = signIn ? `${signIn.tokenType} ${signIn.accessToken}` : ''

  const handleOpenConversation = (projectId: string, id: string) =>
    navigate(`/repositories/${projectId}/conversations/${id}`)

  const handleExpiredToken = useCallback(() => {
    setSignInError('Your RepoAI access token expired. Please sign in again.')
    setProjects([])
    setProjectLoadStatus('idle')
    setIsProjectFormOpen(false)
    setSignIn(null)
  }, [])

  const signInWithGoogle = useCallback(async (idToken: string) => {
    setSignInError('')
    setIsSigningIn(true)

    try {
      const response = await fetch(apiBaseUrl + '/api/v1/auth/google', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ idToken }),
      })

      if (!response.ok) {
        let problem: ProblemDetails = {}
        try {
          problem = (await response.json()) as ProblemDetails
        } catch {
          // Some proxy or network failures do not return a Problem Details body.
        }
        throw new Error(explainProblem(problem, 'Google sign-in could not be completed.'))
      }

      setProjects([])
      setProjectLoadError('')
      setProjectLoadStatus('loading')
      setSignIn((await response.json()) as RepoAiSignInResponse)
    } catch (error) {
      setSignInError(
        error instanceof TypeError
          ? 'Could not reach RepoAI.Api. Make sure it is running on port 5080.'
          : error instanceof Error
            ? error.message
            : 'Google sign-in could not be completed.',
      )
    } finally {
      setIsSigningIn(false)
    }
  }, [])

  useEffect(() => {
    if (!googleClientId || !googleButtonContainer) return

    let active = true

    loadGoogleIdentityScript()
      .then(() => {
        if (!active) return
        const identity = window.google?.accounts?.id
        if (!identity) throw new Error('Google Sign-In loaded but its client was unavailable.')

        if (!googleIdentityInitialized.current) {
          identity.initialize({
            client_id: googleClientId,
            auto_select: false,
            callback: (response) => {
              if (!response.credential) {
                setSignInError('Google did not return an ID token. Please try again.')
                return
              }
              void signInWithGoogle(response.credential)
            },
          })
          googleIdentityInitialized.current = true
        }
        googleButtonContainer.replaceChildren()
        identity.renderButton(googleButtonContainer, {
          theme: 'outline',
          size: 'large',
          shape: 'pill',
          text: 'signin_with',
          width: 270,
        })
      })
      .catch((error: unknown) => {
        if (active) {
          setGoogleScriptError(
            error instanceof Error ? error.message : 'Could not load Google Sign-In.',
          )
        }
      })

    return () => {
      active = false
      googleButtonContainer.replaceChildren()
    }
  }, [googleButtonContainer, googleClientId, signInWithGoogle])

  useEffect(() => {
    const controller = new AbortController()

    fetch(`${apiBaseUrl}/api/v1/status`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error('API is not responding')
        return (await response.json()) as ApiStatus
      })
      .then((result) => {
        setApiStatus('online')
        setApiMessage(`${result.application} · ${result.status}`)
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setApiStatus('offline')
        setApiMessage('API unavailable — start the .NET API on port 5080')
      })

    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!signIn) return

    const controller = new AbortController()

    fetch(`${apiBaseUrl}/api/v1/projects`, {
      headers: { Authorization: `${signIn.tokenType} ${signIn.accessToken}` },
      signal: controller.signal,
    })
      .then(async (response) => {
        if (response.status === 401) {
          setSignInError('Your RepoAI access token expired. Please sign in again.')
          setProjects([])
          setProjectLoadStatus('idle')
          setIsProjectFormOpen(false)
          setSignIn(null)
          return null
        }
        if (!response.ok) {
          let problem: ProblemDetails = {}
          try {
            problem = (await response.json()) as ProblemDetails
          } catch {
            // Some network or proxy errors do not return a Problem Details body.
          }
          throw new Error(explainProblem(problem, 'Could not load your projects.'))
        }
        return (await response.json()) as Project[]
      })
      .then((result) => {
        if (result) {
          setProjects(result)
          setProjectLoadStatus('loaded')
        }
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setProjectLoadError(
          error instanceof TypeError
            ? 'Could not reach RepoAI.Api. Check that the API is running.'
            : error instanceof Error
              ? error.message
              : 'Could not load your projects.',
        )
        setProjectLoadStatus('error')
      })

    return () => controller.abort()
  }, [projectReloadKey, signIn])

  async function validateRepository(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setValidation(null)
    setValidationError('')
    setIsValidating(true)

    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/repositories/validate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ repositoryUrl }),
      })

      if (!response.ok) {
        const problem = (await response.json()) as ProblemDetails
        throw new Error(explainProblem(problem, 'Could not validate this repository URL.'))
      }

      setValidation((await response.json()) as RepositoryValidation)
    } catch (error) {
      setValidationError(
        error instanceof TypeError
          ? 'Could not reach the API. Make sure RepoAI.Api is running on port 5080.'
          : error instanceof Error
            ? error.message
            : 'Something went wrong while validating the URL.',
      )
    } finally {
      setIsValidating(false)
    }
  }

  async function createProject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!signIn) return

    setCreateProjectError('')
    setIsCreatingProject(true)

    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/projects`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `${signIn.tokenType} ${signIn.accessToken}`,
        },
        body: JSON.stringify({ name: projectName, repositoryUrl: projectRepositoryUrl }),
      })

      if (response.status === 401) {
        setSignInError('Your RepoAI access token expired. Please sign in again.')
        setProjects([])
        setProjectLoadStatus('idle')
        setIsProjectFormOpen(false)
        setSignIn(null)
        return
      }
      if (!response.ok) {
        let problem: ProblemDetails = {}
        try {
          problem = (await response.json()) as ProblemDetails
        } catch {
          // Some network or proxy errors do not return a Problem Details body.
        }
        throw new Error(explainProblem(problem, 'Could not create this project.'))
      }

      const createdProject = (await response.json()) as Project
      setProjects((currentProjects) => [createdProject, ...currentProjects])
      setProjectName('')
      setProjectRepositoryUrl('')
      setIsProjectFormOpen(false)
    } catch (error) {
      setCreateProjectError(
        error instanceof TypeError
          ? 'Could not reach RepoAI.Api. Check that the API is running.'
          : error instanceof Error
            ? error.message
            : 'Could not create this project.',
      )
    } finally {
      setIsCreatingProject(false)
    }
  }

  const workspaceLink = (path: string, label: string, icon: 'grid' | 'folder' | 'film' | 'chat', active: boolean) => (
    <a className={`nav-link ${active ? 'active' : ''}`} href={`#${path}`} key={label}>
      <Icon name={icon} />{label}
    </a>
  )

  const projectForm = signIn && isProjectFormOpen && (
    <form className="project-form" onSubmit={createProject}>
      <label htmlFor="project-name">Project name</label>
      <input id="project-name" type="text" required minLength={1} maxLength={200}
        placeholder="e.g. Payments service" value={projectName}
        onChange={(event) => setProjectName(event.target.value)} />
      <label htmlFor="project-repository-url">Git repository URL</label>
      <input id="project-repository-url" type="url" required maxLength={2048}
        placeholder="https://github.com/owner/repository" value={projectRepositoryUrl}
        onChange={(event) => setProjectRepositoryUrl(event.target.value)} />
      <button type="submit" disabled={isCreatingProject}>{isCreatingProject ? 'Creating…' : 'Create project'}</button>
      {createProjectError && <p className="project-form-error" role="alert">{createProjectError}</p>}
    </form>
  )

  const accountPanel = (
    <aside className="setup-card account-panel">
      <div className="setup-icon"><Icon name="spark" /></div>
      <div className="section-kicker">ACCOUNT</div>
      <h2>{signIn ? 'You’re signed in' : 'Make it yours'}</h2>
      {signIn ? (
        <>
          <p className="signed-in-name">{signIn.displayName}</p>
          {signIn.email && <p className="signed-in-email">{signIn.email}</p>}
          <div className="setup-divider" />
          <p className="setup-detail">Your RepoAI access token is held in this tab’s memory only and expires after 15 minutes.</p>
          <button className="clear-sign-in" type="button" onClick={() => {
            window.google?.accounts?.id?.disableAutoSelect()
            setSignIn(null)
            setProjects([])
            setProjectLoadStatus('idle')
            setProjectLoadError('')
            setIsProjectFormOpen(false)
          }}>Clear sign-in</button>
        </>
      ) : (
        <>
          <p>Google sign-in connects your account to a private RepoAI workspace.</p>
          <div className="setup-status">
            <span className={googleClientId ? 'setup-status-dot configured' : 'setup-status-dot'} />
            {googleClientId ? 'Ready to connect with Google' : 'Google Client ID is missing'}
          </div>
          {googleClientId ? <>
            <div className="google-button" ref={setGoogleButtonContainer} aria-busy={isSigningIn} />
            {isSigningIn && <p className="auth-progress" role="status">Verifying your Google account…</p>}
            {googleScriptError && <p className="auth-error" role="alert">{googleScriptError}</p>}
            {signInError && <p className="auth-error" role="alert">{signInError}</p>}
            <p className="setup-detail">Google verifies your account; RepoAI creates a short-lived token for this tab.</p>
          </> : <p className="setup-detail">Set VITE_GOOGLE_CLIENT_ID in frontend/.env.local, then restart Vite to enable sign-in.</p>}
        </>
      )}
    </aside>
  )

  const renderProjectStatus = () => {
    if (!signIn) return <div className="library-empty-state"><strong>Sign in to view your repositories</strong><p>Your repositories and conversations are private to your RepoAI account.</p><a className="inline-action" href="#/overview">Go to Overview to sign in</a></div>
    if (projectLoadStatus === 'loading' || projectLoadStatus === 'idle') return <p className="library-message">Loading your repositories…</p>
    if (projectLoadStatus === 'error') return <div className="project-load-error" role="alert"><p>{projectLoadError}</p><button type="button" onClick={() => { setProjectLoadError(''); setProjectLoadStatus('loading'); setProjectReloadKey((key) => key + 1) }}>Try again</button></div>
    if (projects.length === 0) return <div className="library-empty-state"><strong>Your first repository starts here</strong><p>Create a project to save a repository in your private workspace.</p></div>
    return <div className="repository-card-grid">{projects.map((project) => (
      <article className="repository-library-card" key={project.id}>
        <div className="repository-card-icon"><Icon name="folder" /></div>
        <div className="repository-card-copy"><span className="section-kicker">REPOSITORY</span><h2>{project.name}</h2><a href={project.repositoryUrl} target="_blank" rel="noreferrer">{project.repositoryUrl}</a><small>Added {new Date(project.createdAtUtc).toLocaleDateString()}</small></div>
        <a className="repository-card-open" href={`#/repositories/${project.id}`} aria-label={`Open ${project.name}`}>Open repository <span>→</span></a>
      </article>
    ))}</div>
  }

  const gatedPage = (destination: string) => <div className="library-empty-state"><strong>Sign in to continue</strong><p>Your RepoAI workspace is private to your account.</p><a className="inline-action" href="#/overview">Go to Overview to sign in</a><button className="text-action" type="button" onClick={() => navigate(destination)}>Stay on this page</button></div>

  let pageTitle = routeIsOverview ? 'Overview' : routeIsRepositories ? 'Repositories' : routeIsConversations ? 'Conversations' : routeIsStoryboards ? 'Storyboards' : selectedProject?.name ?? 'Repository'
  if (isRepositoryRoute && repositoryPage === 'analysis') pageTitle = `${selectedProject?.name ?? 'Repository'} · Analysis`
  if (isConversationRoute) pageTitle = isChatRoute ? 'Conversation' : `${selectedProject?.name ?? 'Repository'} · Conversations`
  if (isRepositoryRoute && repositoryPage === 'storyboards') pageTitle = `${selectedProject?.name ?? 'Repository'} · Storyboards`

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="#/overview" aria-label="RepoAI home"><span className="brand-mark"><Icon name="mark" /></span><span>repo<span className="brand-accent">ai</span></span></a>
        <div className="sidebar-label">WORKSPACE</div>
        <nav className="main-nav" aria-label="Main navigation">
          {workspaceLink('/overview', 'Overview', 'grid', routeIsOverview)}
          {workspaceLink('/repositories', 'Repositories', 'folder', routeIsRepositories || isRepositoryRoute)}
          {workspaceLink('/storyboards', 'Storyboards', 'grid', routeIsStoryboards)}
          {workspaceLink('/conversations', 'Conversations', 'chat', routeIsConversations || isConversationRoute)}
        </nav>
        <div className="sidebar-bottom">
          <div className="sidebar-note"><span className="note-icon"><Icon name="spark" /></span><p>Your codebase,<br /><strong>made understandable.</strong></p></div>
          <a className="github-link" href="https://github.com" target="_blank" rel="noreferrer"><Icon name="github" /> Built for GitHub repositories <Icon name="arrow" /></a>
          <div className="sidebar-version">REPOAI · EARLY ACCESS</div>
        </div>
      </aside>

      <main className="main-content">
        <header className="topbar">
          <div className="breadcrumb"><a href="#/overview">Workspace</a><span className="breadcrumb-slash">/</span><strong>{pageTitle}</strong></div>
          <div className={`api-pill ${apiStatus}`}><span className="status-dot" />{apiMessage}</div>
        </header>

        <div className="page-content">
          {routeIsOverview && <>
            <section className="welcome-row">
              <div><div className="eyebrow"><span className="eyebrow-line" /> REPOSITORY INTELLIGENCE, REIMAGINED</div><h1>Every codebase has<br /><span>a story to tell.</span></h1><p className="welcome-copy">Turn complex repositories into clear architecture explanations and answers you can verify against source evidence.</p></div>
              <div className="hero-art" aria-hidden="true"><div className="orbit orbit-one" /><div className="orbit orbit-two" /><div className="hero-core"><Icon name="mark" /></div><div className="orbit-node node-one" /><div className="orbit-node node-two" /><div className="orbit-node node-three" /><span className="hero-caption">CODE → CLARITY</span></div>
            </section>
            <section className="connect-card" aria-labelledby="connect-title">
              <div className="connect-heading"><div className="step-number">01</div><div><div className="section-kicker">START WITH A REPOSITORY</div><h2 id="connect-title">Bring your codebase into focus</h2><p>Paste a public Git repository URL to check its format before importing.</p></div></div>
              <form className="repository-form" onSubmit={validateRepository}>
                <label className="visually-hidden" htmlFor="repository-url">Git repository URL</label><span className="input-icon"><Icon name="github" /></span>
                <input id="repository-url" type="url" required maxLength={2048} placeholder="https://github.com/owner/repository" value={repositoryUrl} onChange={(event) => { setRepositoryUrl(event.target.value); setValidation(null); setValidationError('') }} />
                <button type="submit" disabled={isValidating || apiStatus === 'offline'}>{isValidating ? 'Checking…' : 'Check repository'} <Icon name="arrow" /></button>
              </form>
              <p className="form-footnote"><span className="lock-icon">◇</span> URL format check only. RepoAI has not fetched or imported this repository.</p>
              {validation && <div className="feedback success" role="status"><span className="feedback-icon">✓</span><div><strong>URL format looks good</strong><span>{validation.host} · Reachability was not checked</span></div></div>}
              {validationError && <div className="feedback error" role="alert">{validationError}</div>}
            </section>
            <section className="overview-shortcuts">
              <article><span className="section-kicker">YOUR WORKSPACE</span><strong>{signIn ? `${projects.length} repositories` : 'Private workspace'}</strong><a href="#/repositories">Browse repositories <span>→</span></a></article>
              <article><span className="section-kicker">SAVED CHATS</span><strong>Repository conversations</strong><a href="#/conversations">Open conversations <span>→</span></a></article>
              <article><span className="section-kicker">LLM EXPLANATIONS</span><strong>Evidence-grounded storyboards</strong><a href="#/storyboards">Explore storyboards <span>→</span></a></article>
            </section>
            {accountPanel}
          </>}

          {routeIsRepositories && <section className="library-page" aria-labelledby="repositories-title">
            <div className="library-heading-row"><div><span className="section-kicker">YOUR CODEBASES</span><h1 id="repositories-title">Repositories</h1><p>Each repository has its own analysis, storyboards, and conversations.</p></div><div className="project-heading-actions"><span className="count-badge">{projects.length}</span>{signIn && <button className="new-project-button" type="button" onClick={() => { setCreateProjectError(''); setIsProjectFormOpen((open) => !open) }}>{isProjectFormOpen ? 'Cancel' : 'Add repository'}</button>}</div></div>
            {projectForm}
            {renderProjectStatus()}
          </section>}

          {routeIsConversations && (signIn
            ? <ConversationLibraryPage projects={projects} authorization={authorization} onUnauthorized={handleExpiredToken} onOpenConversation={handleOpenConversation} />
            : gatedPage('/conversations'))}

          {routeIsStoryboards && !isRepositoryRoute && <section className="library-page"><div className="library-heading-row"><div><span className="section-kicker">LLM EXPLANATIONS</span><h1>Storyboards</h1><p>Choose a repository to create and review an evidence-grounded architecture explanation.</p></div></div>{signIn && projects.length > 0 ? <div className="repository-card-grid">{projects.map((project) => <article className="repository-library-card" key={project.id}><div className="repository-card-icon"><Icon name="grid" /></div><div className="repository-card-copy"><span className="section-kicker">ARCHITECTURE STORYBOARD</span><h2>{project.name}</h2><a href={project.repositoryUrl} target="_blank" rel="noreferrer">{project.repositoryUrl}</a></div><a className="repository-card-open" href={`#/repositories/${project.id}/storyboards`}>Open storyboard <span>→</span></a></article>)}</div> : !signIn ? gatedPage('/storyboards') : <div className="library-empty-state"><strong>No repositories yet</strong><p>Add a repository to create its storyboard.</p><a className="inline-action" href="#/repositories">Choose a repository</a></div>}</section>}

          {isRepositoryRoute && !signIn && gatedPage(`/repositories/${routeSegments[1]}`)}
          {isRepositoryRoute && signIn && !selectedProject && (projectLoadStatus === 'loading' || projectLoadStatus === 'idle'
            ? <p className="library-message">Loading repository…</p>
            : <div className="library-empty-state"><strong>Repository not found</strong><p>It may have been removed, or it may belong to another account.</p><a className="inline-action" href="#/repositories">Back to repositories</a></div>)}

          {isRepositoryRoute && selectedProject && <>
            <section className="repository-workspace-heading"><div className="repository-workspace-icon"><Icon name="folder" /></div><div><span className="section-kicker">REPOSITORY WORKSPACE</span><h1>{selectedProject.name}</h1><a href={selectedProject.repositoryUrl} target="_blank" rel="noreferrer">{selectedProject.repositoryUrl}</a></div><a className="repository-back-link" href="#/repositories">← All repositories</a></section>
            <nav className="repository-tabs" aria-label="Repository sections">
              <a className={repositoryPage === 'overview' ? 'active' : ''} href={`#/repositories/${selectedProject.id}`}>Overview</a>
              <a className={repositoryPage === 'analysis' ? 'active' : ''} href={`#/repositories/${selectedProject.id}/analysis`}>Repository analysis</a>
              <a className={isConversationRoute ? 'active' : ''} href={`#/repositories/${selectedProject.id}/conversations`}>Conversations</a>
              <a className={repositoryPage === 'storyboards' ? 'active' : ''} href={`#/repositories/${selectedProject.id}/storyboards`}>Storyboards</a>
            </nav>
            {repositoryPage === 'overview' && <section className="repository-overview-card"><div><span className="section-kicker">PROJECT OVERVIEW</span><h2>Understand this codebase</h2><p>Import a snapshot to inspect its files and architecture, or open a conversation to ask evidence-grounded questions.</p></div><div className="repository-overview-actions"><a href={`#/repositories/${selectedProject.id}/analysis`}>Import &amp; analyze <span>→</span></a><a href={`#/repositories/${selectedProject.id}/conversations`}>Open conversations <span>→</span></a><a href={`#/repositories/${selectedProject.id}/storyboards`}>Create a storyboard <span>→</span></a></div></section>}
            {repositoryPage === 'analysis' && signIn && <ProjectImportPanel key={selectedProject.id} projectId={selectedProject.id} authorization={authorization} onUnauthorized={handleExpiredToken} />}
            {isConversationRoute && signIn && <ProjectChatPanel key={`${selectedProject.id}-${conversationId ?? 'list'}`} projectId={selectedProject.id} authorization={authorization} onUnauthorized={handleExpiredToken} view={isChatRoute ? 'chat' : 'list'} conversationId={conversationId} onOpenConversation={(id) => handleOpenConversation(selectedProject.id, id)} onBack={() => navigate(`/repositories/${selectedProject.id}/conversations`)} />}
            {repositoryPage === 'storyboards' && signIn && <ProjectStoryboardPage key={selectedProject.id} projectId={selectedProject.id} authorization={authorization} onUnauthorized={handleExpiredToken} />}
          </>}

          <footer className="page-footer"><span>Built to make the architecture behind the code visible.</span><span>REPOAI © 2026</span></footer>
        </div>
      </main>
    </div>
  )
}

export default App
