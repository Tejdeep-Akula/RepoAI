import { useEffect, useState } from 'react'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080').replace(/\/$/, '')

type Project = { id: string; name: string; repositoryUrl: string }
type Conversation = { id: string; title: string; updatedAtUtc: string }
type ConversationGroup = { project: Project; conversations: Conversation[] }

export default function ConversationLibraryPage({
  projects,
  authorization,
  onUnauthorized,
  onOpenConversation,
}: {
  projects: Project[]
  authorization: string
  onUnauthorized: () => void
  onOpenConversation: (projectId: string, conversationId: string) => void
}) {
  const [groups, setGroups] = useState<ConversationGroup[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [refreshKey, setRefreshKey] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    const load = async () => {
      setLoading(true)
      setError('')
      try {
        const results = await Promise.all(projects.map(async (project) => {
          const response = await fetch(`${apiBaseUrl}/api/v1/projects/${project.id}/conversations`, {
            headers: { Authorization: authorization },
            signal: controller.signal,
          })
          if (response.status === 401) {
            onUnauthorized()
            throw new Error('Your sign-in expired. Please sign in again.')
          }
          if (!response.ok) throw new Error(`Could not load conversations for ${project.name}.`)
          return { project, conversations: await response.json() as Conversation[] }
        }))
        if (!controller.signal.aborted) setGroups(results.filter((group) => group.conversations.length > 0))
      } catch (cause) {
        if (cause instanceof DOMException && cause.name === 'AbortError') return
        if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'Could not load conversations.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [authorization, onUnauthorized, projects, refreshKey])

  return (
    <section className="library-page" aria-labelledby="conversation-library-title">
      <div className="library-heading-row">
        <div>
          <span className="section-kicker">YOUR REPOSITORY CHATS</span>
          <h1 id="conversation-library-title">Conversations</h1>
          <p>Pick up where you left off, grouped by repository.</p>
        </div>
        <button className="snapshot-refresh" type="button" disabled={loading} onClick={() => setRefreshKey((key) => key + 1)}>Refresh</button>
      </div>
      {loading && <p className="library-message">Loading conversations…</p>}
      {error && <p className="chat-error" role="alert">{error}</p>}
      {!loading && !error && groups.length === 0 && (
        <div className="library-empty-state">
          <span className="library-empty-icon">✦</span>
          <strong>No conversations yet</strong>
          <p>Open a repository and start a conversation to ask questions about its code.</p>
        </div>
      )}
      <div className="conversation-groups">
        {groups.map(({ project, conversations }) => (
          <section className="conversation-group" key={project.id}>
            <header>
              <div><span className="section-kicker">REPOSITORY</span><h2>{project.name}</h2><a href={project.repositoryUrl} target="_blank" rel="noreferrer">{project.repositoryUrl}</a></div>
              <span className="count-badge">{conversations.length}</span>
            </header>
            <div className="conversation-list">
              {conversations.map((conversation) => (
                <button className="conversation-list-card" type="button" key={conversation.id}
                  onClick={() => onOpenConversation(project.id, conversation.id)}>
                  <span className="conversation-list-icon">↗</span>
                  <span className="conversation-list-copy"><strong>{conversation.title}</strong><small>Updated {new Date(conversation.updatedAtUtc).toLocaleString()}</small></span>
                  <span className="conversation-list-arrow">→</span>
                </button>
              ))}
            </div>
          </section>
        ))}
      </div>
    </section>
  )
}
