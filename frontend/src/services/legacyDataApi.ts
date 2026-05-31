import axios from 'axios'

const api = axios.create({
  baseURL: '/api/admin'
})

export default {
  async scanLegacyData(params: { page?: number; pageSize?: number }) {
    const response = await api.get('/legacy-data/scan', { params })
    return response.data
  },

  async cleanLegacyData(uploadRecordId: string) {
    const response = await api.post('/legacy-data/clean', {
      uploadRecordId
    })
    return response.data
  }
}
