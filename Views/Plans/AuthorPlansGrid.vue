<template>
    <div class="p-4">
        <h2 class="text-xl font-bold mb-4">Author Plans</h2>

        <!-- Search box -->
        <input v-model="searchQuery"
               type="text"
               placeholder="Search by Plan Name..."
               class="border rounded px-3 py-2 mb-3 w-1/3" />

        <!-- Table -->
        <table class="min-w-full bg-white border rounded shadow">
            <thead class="bg-gray-100">
                <tr>
                    <th class="p-2 cursor-pointer" @click="sortBy('planName')">Plan Name</th>
                    <th class="p-2 cursor-pointer" @click="sortBy('price')">Price</th>
                    <th class="p-2 cursor-pointer" @click="sortBy('durationInDays')">Duration</th>
                    <th class="p-2">Start Date</th>
                    <th class="p-2">End Date</th>
                    <th class="p-2">Status</th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="plan in paginatedPlans"
                    :key="plan.authorPlanId"
                    class="border-t hover:bg-gray-50">
                    <td class="p-2">{{ plan.plan?.planName }}</td>
                    <td class="p-2">${{ plan.plan?.price.toFixed(2) }}</td>
                    <td class="p-2">{{ plan.plan?.durationInDays }} days</td>
                    <td class="p-2">{{ formatDate(plan.startDate) }}</td>
                    <td class="p-2">{{ formatDate(plan.endDate) }}</td>
                    <td class="p-2">
                        <span class="px-2 py-1 rounded text-white"
                              :class="plan.isActive ? 'bg-green-500' : 'bg-red-500'">
                            {{ plan.isActive ? "Active" : "Inactive" }}
                        </span>
                    </td>
                </tr>
            </tbody>
        </table>

        <!-- Pagination -->
        <div class="flex justify-between items-center mt-4">
            <button @click="prevPage"
                    :disabled="currentPage === 1"
                    class="px-4 py-2 bg-gray-200 rounded disabled:opacity-50">
                Prev
            </button>
            <span>Page {{ currentPage }} of {{ totalPages }}</span>
            <button @click="nextPage"
                    :disabled="currentPage === totalPages"
                    class="px-4 py-2 bg-gray-200 rounded disabled:opacity-50">
                Next
            </button>
        </div>
    </div>
</template>

<script>
import axios from "axios";

export default {
  name: "AuthorPlansGrid",
  data() {
    return {
      plans: [],
      searchQuery: "",
      sortKey: "planName",
      sortOrder: "asc",
      currentPage: 1,
      pageSize: 5,
    };
  },
  computed: {
    filteredPlans() {
      let result = this.plans;

      if (this.searchQuery) {
        result = result.filter((p) =>
          p.plan?.planName
            ?.toLowerCase()
            .includes(this.searchQuery.toLowerCase())
        );
      }

      return result.sort((a, b) => {
        const valA = this.getValue(a, this.sortKey);
        const valB = this.getValue(b, this.sortKey);

        if (valA < valB) return this.sortOrder === "asc" ? -1 : 1;
        if (valA > valB) return this.sortOrder === "asc" ? 1 : -1;
        return 0;
      });
    },
    totalPages() {
      return Math.ceil(this.filteredPlans.length / this.pageSize);
    },
    paginatedPlans() {
      const start = (this.currentPage - 1) * this.pageSize;
      return this.filteredPlans.slice(start, start + this.pageSize);
    },
  },
  methods: {
    async fetchPlans() {
      const res = await axios.get("/api/authorplans"); // <-- Backend API
      this.plans = res.data;
    },
    formatDate(dateStr) {
      return new Date(dateStr).toLocaleDateString();
    },
    getValue(obj, key) {
      if (key === "planName") return obj.plan?.planName || "";
      if (key === "price") return obj.plan?.price || 0;
      if (key === "durationInDays") return obj.plan?.durationInDays || 0;
      return obj[key];
    },
    sortBy(key) {
      if (this.sortKey === key) {
        this.sortOrder = this.sortOrder === "asc" ? "desc" : "asc";
      } else {
        this.sortKey = key;
        this.sortOrder = "asc";
      }
    },
    nextPage() {
      if (this.currentPage < this.totalPages) this.currentPage++;
    },
    prevPage() {
      if (this.currentPage > 1) this.currentPage--;
    },
  },
  mounted() {
    this.fetchPlans();
  },
};
</script>
