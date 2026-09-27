<template>
  <div class="adm-fade-in portal-user-view">
    <PageHeader title="助教管理" description="管理助教账户、学科授权和权限撤销">
      <template #actions>
        <el-button type="primary" :icon="Plus" @click="openGrantDialog">授予助教权限</el-button>
      </template>
    </PageHeader>

    <FilterBar @search="onSearch" @reset="resetFilters">
      <el-form-item>
        <el-input
          v-model="searchKeyword"
          class="filter-keyword"
          :prefix-icon="Search"
          placeholder="搜索助教姓名 / 手机号 / 用户ID"
          clearable
          @keyup.enter="onSearch"
        />
      </el-form-item>
      <el-form-item>
        <el-select v-model="searchSubject" class="filter-control" placeholder="全部学科" clearable @change="onSearch">
          <el-option v-for="s in availableSubjects" :key="s.value" :label="s.name" :value="s.value" />
        </el-select>
      </el-form-item>
    </FilterBar>

    <DataTableCard
      :data="assistants"
      :loading="loading"
      :error="loadError"
      empty-text="暂无助教数据"
      :row-key="rowKey"
      @retry="loadAssistants"
    >
      <el-table-column label="头像" width="72">
        <template #default="{ row }">
          <div class="user-avatar" :style="{ background: getAvatarGradient(getDisplayName(row)) }">{{ getAvatarChar(getDisplayName(row)) }}</div>
        </template>
      </el-table-column>
      <el-table-column label="ID" min-width="200">
        <template #default="{ row }"><span class="mono-id">{{ row.userId || '-' }}</span></template>
      </el-table-column>
      <el-table-column label="手机号" width="140">
        <template #default="{ row }"><span class="phone-cell">{{ getPhone(row) }}</span></template>
      </el-table-column>
      <el-table-column label="用户名" min-width="120">
        <template #default="{ row }"><span class="user-name">{{ getDisplayName(row) }}</span></template>
      </el-table-column>
      <el-table-column label="备注" min-width="140">
        <template #default="{ row }">
          <span v-if="getRemark(row)" class="note-cell" :title="getRemark(row)">{{ getRemark(row) }}</span>
          <span v-else class="note-cell empty">-</span>
        </template>
      </el-table-column>
      <el-table-column label="学科" min-width="200">
        <template #default="{ row }">
          <div v-if="row.subjects && row.subjects.length > 0" class="subj-tags">
            <SubjectTag v-for="subj in row.subjects" :key="subj" :subject="subj" closable @close="removeSubject(row, subj)" />
          </div>
          <el-tag v-else type="warning" size="small"><el-icon class="tag-icon"><Warning /></el-icon>未分配学科</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="130">
        <template #default="{ row }"><span class="time-cell">{{ formatDate(row.createdAt) }}</span></template>
      </el-table-column>
      <el-table-column label="操作" width="200" align="right" fixed="right">
        <template #default="{ row }">
          <el-button v-if="row.subjects && row.subjects.length > 0" link type="primary" @click="openSubjectsDialog(row)">学科</el-button>
          <el-button v-else type="primary" size="small" :icon="Plus" @click="openSubjectsDialog(row)">分配</el-button>
          <el-button link type="danger" @click="revokeAssistant(row)">撤销权限</el-button>
        </template>
      </el-table-column>
      <template v-if="assistants.length > 0" #footer>
        <ListPagination v-model:page="page" v-model:page-size="pageSize" :total="total" @change="onPageChange" />
      </template>
    </DataTableCard>

    <!-- Grant Permission -->
    <el-dialog append-to-body v-model="showGrantDialog" title="授予助教权限" width="600px" :close-on-click-modal="false" @close="closeGrantDialog">
      <el-form label-position="top" @submit.prevent>
        <el-form-item label="选择用户" required>
          <el-select
            v-model="grantForm.selectedUserId"
            filterable
            remote
            clearable
            :remote-method="onGrantUserQuery"
            placeholder="输入手机号或用户名搜索（至少 2 个字符）"
            no-data-text="无匹配用户"
            class="full-width"
            @change="onGrantUserChange"
          >
            <el-option v-for="u in grantUserOptions" :key="u.userId" :label="u.username || u.displayName || u.userId" :value="u.userId">
              <span class="option-name">{{ u.username || u.displayName || u.userId }}</span>
              <span class="option-meta">{{ u.phone || '无手机号' }}<template v-if="u.displayName && u.displayName !== u.username"> · {{ u.displayName }}</template></span>
            </el-option>
          </el-select>
          <div v-if="grantForm.selectedUserId && selectedUser" class="selected-user-banner full-width">
            <div class="mini-avatar" :style="{ background: getAvatarGradient(selectedUser.username || selectedUser.userId) }">{{ getAvatarChar(selectedUser.username || selectedUser.userId) }}</div>
            <div class="sel-info">
              <div class="sel-name">{{ selectedUser.username }}</div>
              <div class="sel-meta">{{ selectedUser.phone || '无手机号' }}<span v-if="selectedUser.remark"> · {{ selectedUser.remark }}</span></div>
            </div>
          </div>
        </el-form-item>
        <el-form-item label="授权学科" required>
          <el-checkbox-group v-model="grantForm.selectedSubjects" class="subject-checks">
            <el-checkbox v-for="s in availableSubjects" :key="s.value" :value="s.value" border>{{ s.name }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item v-if="selectedUser && selectedUser.remark" label="备注">
          <div class="readonly-note full-width">{{ selectedUser.remark }}</div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="closeGrantDialog">取消</el-button>
        <el-button type="primary" :loading="granting" @click="grantAssistant">确认授权</el-button>
      </template>
    </el-dialog>

    <!-- Manage Subjects -->
    <el-dialog append-to-body v-model="showSubjectsDialogVisible" title="学科管理" width="560px" :close-on-click-modal="false">
      <div v-if="currentAssistant" class="user-info-banner">
        <div class="user-avatar" :style="{ background: getAvatarGradient(getDisplayName(currentAssistant)) }">{{ getAvatarChar(getDisplayName(currentAssistant)) }}</div>
        <div>
          <div class="info-name">{{ getDisplayName(currentAssistant) }}</div>
          <div class="info-sub">{{ getPhone(currentAssistant) }} · 当前 {{ currentAssistant.subjects.length }} 个学科</div>
        </div>
      </div>
      <el-form label-position="top" @submit.prevent>
        <el-form-item label="授权学科">
          <el-checkbox-group v-model="selectedSubjects" class="subject-checks">
            <el-checkbox v-for="s in availableSubjects" :key="s.value" :value="s.value" border>{{ s.name }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showSubjectsDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="savingSubjects" @click="saveAssistantSubjects">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Plus, Search, Warning } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { confirmDanger } from '../utils/confirm'
import {
  assistantPortalClient,
  getAssistantPortalErrorMessage,
  type AssistantAccountDto,
  type SubjectOption,
} from '../services/assistantPortalApi'
import { getIdentityAdminApiClient, type IdentityUser } from '../services/identityApi'
import {
  getSubjectLabel,
  getAvatarGradient, getAvatarChar, formatDate,
} from '../utils/subject'
import PageHeader from '../components/list/PageHeader.vue'
import FilterBar from '../components/list/FilterBar.vue'
import DataTableCard from '../components/list/DataTableCard.vue'
import ListPagination from '../components/list/ListPagination.vue'
import SubjectTag from '../components/list/SubjectTag.vue'

const assistants = ref<AssistantAccountDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const rowKey = (row: AssistantAccountDto) => row.userId || String(row.id)
const identityUserMap = ref<Record<string, IdentityUser>>({})
const availableSubjects = ref<SubjectOption[]>([])

const searchKeyword = ref('')
const searchSubject = ref<number | undefined>(undefined)
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

const showGrantDialog = ref(false)
const grantForm = ref({ keyword: '', selectedUserId: '', selectedSubjects: [] as number[] })
const searchResults = ref<IdentityUser[]>([])
const granting = ref(false)

const showSubjectsDialogVisible = ref(false)
const currentAssistant = ref<AssistantAccountDto | null>(null)
const selectedSubjects = ref<number[]>([])
const savingSubjects = ref(false)

// Keep the selected account separate from searchResults. Selecting an account
// clears the dropdown results, so deriving this value from searchResults made
// the selection banner disappear immediately after the click.
const selectedUser = ref<IdentityUser | null>(null)

function getDisplayName(a: AssistantAccountDto): string {
  if (a.username) return a.username
  if (a.userId && identityUserMap.value[a.userId]) {
    return identityUserMap.value[a.userId].displayName || identityUserMap.value[a.userId].username || a.userId
  }
  return a.userId || `#${a.id}`
}

function getPhone(a: AssistantAccountDto): string {
  if (a.phone) return a.phone
  if (a.userId && identityUserMap.value[a.userId]?.phone) return identityUserMap.value[a.userId].phone
  return '-'
}

function getRemark(a: AssistantAccountDto): string {
  if (a.userId && identityUserMap.value[a.userId]?.remark) return identityUserMap.value[a.userId].remark || ''
  return ''
}

async function loadAssistants() {
  loading.value = true
  loadError.value = null
  try {
    const result = await assistantPortalClient.getAssistants({
      keyword: searchKeyword.value.trim() || undefined,
      subject: searchSubject.value,
      page: page.value,
      pageSize: pageSize.value,
    })
    assistants.value = result.data
    total.value = result.total
    page.value = result.page
    pageSize.value = result.pageSize
    await loadIdentityUsers()
  } catch (error) {
    // Drop stale rows so a failure is never mistaken for an (outdated) result.
    assistants.value = []
    total.value = 0
    loadError.value = '加载助教列表失败'
    ElMessage.error('加载助教列表失败')
  } finally {
    loading.value = false
  }
}

async function loadIdentityUsers() {
  const userIds = assistants.value
    .map(t => t.userId)
    .filter((id): id is string => !!id)
  if (userIds.length === 0) {
    identityUserMap.value = {}
    return
  }
  try {
    const users = await getIdentityAdminApiClient().getUsersByIds(userIds)
    const map: Record<string, IdentityUser> = {}
    for (const u of users) {
      map[u.userId] = u
    }
    identityUserMap.value = map
  } catch (error) {
    console.error('Failed to load identity users:', error)
  }
}

async function loadAvailableSubjects() {
  try {
    const result = await assistantPortalClient.getAvailableSubjects()
    availableSubjects.value = result.data
  } catch (error) {
    console.error('Failed to load subjects:', error)
    ElMessage.error('加载助教学科列表失败')
  }
}

function onSearch() {
  page.value = 1
  loadAssistants()
}

function resetFilters() {
  searchKeyword.value = ''
  searchSubject.value = undefined
  page.value = 1
  loadAssistants()
}

// page / pageSize are already updated by ListPagination (size changes reset to page 1).
function onPageChange() {
  loadAssistants()
}

function openGrantDialog() {
  grantForm.value = { keyword: '', selectedUserId: '', selectedSubjects: [] }
  searchResults.value = []
  selectedUser.value = null
  showGrantDialog.value = true
}

function closeGrantDialog() {
  showGrantDialog.value = false
  grantForm.value = { keyword: '', selectedUserId: '', selectedSubjects: [] }
  searchResults.value = []
  selectedUser.value = null
}

async function searchUsers() {
  if (!grantForm.value.keyword || grantForm.value.keyword.length < 2) {
    searchResults.value = []
    return
  }
  try {
    const keyword = grantForm.value.keyword
    const isPhone = /^\d+$/.test(keyword)
    const result = await getIdentityAdminApiClient().getUsers({
      username: isPhone ? undefined : keyword,
      phone: isPhone ? keyword : undefined,
      page: 1,
      pageSize: 20,
    })
    searchResults.value = result.items
  } catch (error) {
    console.error('Search users failed:', error)
    ElMessage.error('搜索用户失败')
  }
}

// el-select remote-method: same rules as before (>= 2 chars, digits search by phone, pageSize 20).
function onGrantUserQuery(query: string) {
  grantForm.value.keyword = query
  searchUsers()
}

// Keep the selected user among the options so its label survives clearing the results.
const grantUserOptions = computed(() => {
  const selected = selectedUser.value
  if (!selected) return searchResults.value
  return [selected, ...searchResults.value.filter(u => u.userId !== selected.userId)]
})

function onGrantUserChange(userId: string) {
  if (!userId) {
    clearSelectedUser()
    return
  }
  const user = grantUserOptions.value.find(u => u.userId === userId)
  if (user) selectUser(user)
}

function selectUser(u: IdentityUser) {
  grantForm.value.selectedUserId = u.userId
  selectedUser.value = u
  searchResults.value = []
  grantForm.value.keyword = ''
}

function clearSelectedUser() {
  grantForm.value.selectedUserId = ''
  selectedUser.value = null
}


async function grantAssistant() {
  if (!grantForm.value.selectedUserId) {
    ElMessage.warning('请选择用户')
    return
  }
  if (grantForm.value.selectedSubjects.length === 0) {
    ElMessage.warning('请至少选择一个学科')
    return
  }
  granting.value = true
  try {
    await assistantPortalClient.grantAssistant(grantForm.value.selectedUserId, {
      subjects: grantForm.value.selectedSubjects,
    })
    ElMessage.success('授予权限成功')
    closeGrantDialog()
    await loadAssistants()
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error) || '授予权限失败')
  } finally {
    granting.value = false
  }
}

async function revokeAssistant(a: AssistantAccountDto) {
  if (!a.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }
  const confirmed = await confirmDanger({
    title: '确认撤销',
    message: `确认撤销 ${getDisplayName(a)} 的助教权限？该操作将移除其所有学科授权。`,
    confirmText: '撤销',
  })
  if (!confirmed) return
  try {
    await assistantPortalClient.revokeAssistant(a.userId)
    ElMessage.success('撤销权限成功')
    await loadAssistants()
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error) || '撤销权限失败')
  }
}

function openSubjectsDialog(a: AssistantAccountDto) {
  currentAssistant.value = a
  selectedSubjects.value = [...a.subjects]
  showSubjectsDialogVisible.value = true
}


async function removeSubject(a: AssistantAccountDto, subjectId: number) {
  if (!a.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }
  const label = getSubjectLabel(subjectId)
  const confirmed = await confirmDanger({
    title: '确认移除',
    message: `确认移除「${getDisplayName(a)}」的「${label}」学科授权？`,
    confirmText: '移除',
  })
  if (!confirmed) return
  try {
    await assistantPortalClient.removeAssistantSubject(a.userId, subjectId)
    ElMessage.success(`已移除 ${label}`)
    await loadAssistants()
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error) || '删除学科失败')
  }
}

async function saveAssistantSubjects() {
  if (!currentAssistant.value?.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }
  savingSubjects.value = true
  try {
    await assistantPortalClient.setAssistantSubjects(currentAssistant.value.userId, selectedSubjects.value)
    ElMessage.success('保存成功')
    showSubjectsDialogVisible.value = false
    await loadAssistants()
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error) || '保存失败')
  } finally {
    savingSubjects.value = false
  }
}

onMounted(async () => {
  await loadAvailableSubjects()
  await loadAssistants()
})
</script>

<style scoped>
.filter-keyword {
  width: 260px;
}
.filter-control {
  width: 140px;
}
.full-width {
  width: 100%;
}
.subject-checks {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
  width: 100%;
}
.subject-checks :deep(.el-checkbox) {
  margin-right: 0;
}
.option-name {
  margin-right: 8px;
  color: var(--adm-text-primary);
}
.option-meta {
  color: var(--adm-text-tertiary);
  font-size: 12px;
}
.tag-icon {
  margin-right: 4px;
  vertical-align: -2px;
}
</style>
