import httpClient from './httpClient'

/**
 * Wire mirror of the database-hosted application settings API (issue #118,
 * `AppSettingsController`). The DTOs are hand-written copies of the backend
 * contract: upstream changes surface here only as runtime deserialization
 * drift, so they must be kept in sync manually — same rule as the
 * ServiceClients DTOs on the backend side.
 */

/** The layer an effective value was resolved from: database override, valid
 *  static configuration, or the registered options-class default. */
export type SettingSource = 'db' | 'static' | 'default'

/** One effective setting as returned by the list endpoint. `updatedAt` and
 *  `updatedBy` exist only for the `db` source. */
export interface SettingDto {
  key: string
  value: string
  source: SettingSource
  updatedAt?: number
  updatedBy?: string
}

/** The effective setting returned after a write (PUT upsert / DELETE override
 *  both answer with the post-write effective value, same shape as the list). */
export type SettingUpdateResult = SettingDto

/** Request body of the PUT upsert. */
export interface SettingUpdateRequest {
  value: string
}

class SettingsApiClient {
  private client = httpClient

  /** Every registered key with its effective value and source, ordered by key. */
  async listSettings(): Promise<SettingDto[]> {
    const response = await this.client.get<SettingDto[]>('/api/admin/settings')
    return response.data
  }

  /** Upserts the database override; the backend validates against its registry
   *  (400 with a fixed message, 404 for unregistered keys). */
  async updateSetting(key: string, value: string): Promise<SettingUpdateResult> {
    const response = await this.client.put<SettingUpdateResult>(
      `/api/admin/settings/${encodeURIComponent(key)}`,
      { value } satisfies SettingUpdateRequest
    )
    return response.data
  }

  /** Deletes the database override and returns the post-delete effective
   *  setting; 404 when the key has no override row (for example another tab
   *  already removed it — writes are last-writer-wins, no conflict UX). */
  async deleteSetting(key: string): Promise<SettingUpdateResult> {
    const response = await this.client.delete<SettingUpdateResult>(
      `/api/admin/settings/${encodeURIComponent(key)}`
    )
    return response.data
  }
}

export const settingsApi = new SettingsApiClient()

export default settingsApi
